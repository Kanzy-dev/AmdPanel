# Architecture — AmdPanel

## 1. Слои и поток вызовов

```
┌──────────────────────────────────────────────────────────────────┐
│ UI.WPF (AmdPanel.App)                                            │
│   MainWindow.xaml + TreeView + DataGrid (Parameter / Value)      │
│   ViewModels: GpuViewModel, DisplayViewModel, DriverViewModel    │
│   MVVM (CommunityToolkit.Mvvm) — будет в Этапе 2                 │
├──────────────────────────────────────────────────────────────────┤
│ Core.Profiles (Этап 3)        Core.DriverManager (Этап 4)        │
│   ProfileService              DriverManagerService               │
│   JsonProfileRepository       DriverDownloader (amd.com)         │
│   RegistryApplier             CleanInstaller (7z + pnputil)       │
├──────────────────────────────────────────────────────────────────┤
│ Core.Hardware                                                     │
│   AdlxContext                                                     │
│   HardwareScanner  ──►  Capability / EDID / Registry detection    │
│   DisplayModeManager (watchdog 15s)                               │
│   Win32: SetupApiLocator, User32Display, DisplayEdidReader, PnpUtil│
├──────────────────────────────────────────────────────────────────┤
│ AmdPanel.NativeBridge.dll  (C++ flat-C мост, DllImport-exportable)│
│   bridge.cpp / bridge.h                                           │
│   потребляет ADLX SDK (ISystem → IADLXGPUList / IADLXDisplayList) │
├──────────────────────────────────────────────────────────────────┤
│ amdadlx64.dll   (системная AMD DLL, поставляется с драйвером)     │
└──────────────────────────────────────────────────────────────────┘
```

## 2. Почему нужен C++ мост, а не прямой P/Invoke к amdadlx64.dll

ADLX SDK построен по модели COM-подобных C++ интерфейсов: `IADLXSystem`,
`IADLXGPUList`, `IADLXGPU`, `IADLXDisplay`, `IADLXGPUTuning`, …
Каждый интерфейс — это C++ vtable, а не flat C export. Сама
`amdadlx64.dll` экспортирует **только четыре** C-функции:

- `ADLXInitialize(version, IADLXSystem**)`
- `ADLXInitialize2(version, IADLXSystem**, IADLMapping**)`
- `ADLXTerminate()`
- `ADLXQueryVersion(const char**)`
- `ADLXQueryFullVersion(uint64*)`

Чтобы из C# вызвать `gpu->Get3DSettings(&d3); d3->IsSupportedAntiLag(&b);`,
нужно либо вручную Marshall'ить C++ vtable через `Marshal.GetFunctionPointerForDelegate`
для **каждого** метода каждого интерфейса (сотни методов, очень хрупко), либо
написать тонкий C++ мост, который сам держит смарт-указатели
(`IADLXGPUPtr`, `IADLXDisplayPtr` и т.д. из ADLX SDK) и отдаёт наружу
плоские C-функции с POD-структурами.

Мы выбираем **C++ мост**, потому что:

1. Это официальный подход AMD — `ADLXHelper.cpp` сам работает через
   `LoadLibrary("amdadlx64.dll")` и `GetProcAddress`.
2. C# сторона остаётся чистым P/Invoke без сложного vtable-marshalling.
3. Память для ADLX string/int-массивов выделяется в C++ через
   `CoTaskMemAlloc` — это **тот же аллокатор**, что использует .NET
   P/Invoke для `Marshal.FreeCoTaskMem`. Корректное освобождение
   гарантируется функцией `ADLXBridge_FreeBuffer`.

## 3. Жизненный цикл ADLX

- `AdlxContext` — `IDisposable` обёртка над глобальным singleton
  `g_bridgeHelper` в C++ мосте. В C++ стороне есть `g_initialized`
  флаг: повторный `Initialize()` в том же процессе дёшев и безопасен.
- `AdlxInitializationStrategy.Auto` сначала пробует обычный
  `ADLXInitialize`, при ошибке (Polaris с legacy-драйвером) переходит на
  `ADLXInitializeWithIncompatibleDriver`. Это покрывает **обе** ветки
  AMD: современные RDNA-карты и Polaris-legacy.
- `Terminate()` вызывается в `Dispose()`. Порядок важен: все ADLX
  интерфейсы должны быть освобождены ДО `Terminate()` — смарт-указатели
  ADLXPtr (`IADLXGPUPtr`, `IADLXDisplayPtr`) в bridge.cpp автоматически
  освобождаются при выходе из блока функции, поэтому корректность
  гарантирована.

## 4. Watchdog-механизм DisplayModeManager

```
UI вызывает ApplyModeAsync(device, w, h, Hz, bpc)
   │
   ├─► User32Display.QueryCurrentMode(device) → previousMode
   ├─► ChangeDisplaySettingsEx(device, newMode, CDS_TEST) ← Windows test
   ├─► ChangeDisplaySettingsEx(device, newMode, CDS_NORESET)
   ├─► ChangeDisplaySettingsEx(null, NULL, 0) ← flush
   │
   ├─► Запускается System.Threading.Timer(15s)
   ├─► WatchdogCallback?.Invoke(request, controller)
   │       │
   │       └─► UI показывает модальное окно "Сохранить изменения?"
   │               [Да] → controller.Confirm() → таймер гасится, result = ConfirmedByUser=true
   │               [Нет] → controller.Revert() → таймер гасится, RevertTo(previousMode), result = RevertedByUser
   │
   └─► Если таймер сработал раньше подтверждения:
           DoRevert(previousMode) → result = RevertedByWatchdog=true
```

- `--apply-profile` режим **не** показывает модальное окно: если callback
  не зарегистрирован, watchdog сам откатит через 15 секунд. Это безопасно,
  но бесполезно — поэтому для CLI сценария профиль должен
  использовать только `SetScalingMode / SetPixelFormat / SetColorDepth`,
  которые атомарны и не дают чёрного экрана.
- Флаг `CDS_NORESET` в `ChangeDisplaySettingsEx` позволяет накапливать
  изменения в реестре и применять их одним flush-вызовом — это паттерн
  NVIDIA/AMD, иначе Windows может применить промежуточные состояния.

## 5. Динамическая адаптация под монитор и порт

`HardwareScanner.FilterByBandwidth(modes, port, maxBpc)`:

| Порт           | Лимит Gbps | Пояснение                              |
|----------------|-----------|----------------------------------------|
| DisplayPort    | 25.92     | DP 1.4 HBR3 × 4 lanes                  |
| HDMI           | 18.00     | HDMI 2.0 (FRL не активирован)          |
| DVI            | 9.95      | single-link DVI-D                      |
| VGA            | ∞         | аналоговый сигнал, bpc не ограничен    |

Расчёт: `bandwidth = W × H × Hz × bpc × 3 (RGB) / 8 / 1e9 × 1.2 (overhead)`.
Функция консервативна: лучше недодать частоту, чем поймать чёрный экран.
Точные лимиты HDMI 2.1 FRL / DP 2.0 UHBR можно поднять позже, когда UI
будет иметь галочку "высокая пропускная способность кабеля".

## 6. Driver-Only vs Adrenalin — детекция

`HardwareScanner.ScanDriverRegistryAndStatus()`:

1. **SetupApi:** `SetupDiGetClassDevs(DisplayClassGuid, DIGCF_PRESENT)`
   перебирает все активные адаптеры класса Display, для каждого читает
   `SPDRP_HARDWAREID` (REG_MULTI_SZ) и выбирает те, у кого в первой строке
   есть `VEN_1002`. Из них берётся `SPDRP_DRIVER` → это суффикс
   `{ClassGUID}\XXXX`, который присоединяется к
   `SYSTEM\CurrentControlSet\Control\Class\` для формирования полного
   пути к ветке драйвера в реестре. **Никакого хардкода `\0000`.**

2. **Driver-Only check:** если в реестре отсутствуют ключи
   `HKLM\SOFTWARE\AMD\CN` и `HKLM\SOFTWARE\WOW6432Node\AMD\CN`,
   считается, что Adrenalin не установлен — это подтверждает режим
   Driver-Only. Ключ `CN` создаётся установщиком Adrenalin (Catalyst
   Control Center / Adrenalin Edition).

## 7. Память и производительность

- Потребление ОЗУ целевого приложения: < 30 МБ (оценка по аналогичным
  WPF-приложениям с Core.Hardware-слоем; WPF bootstrapping ~15–18 МБ,
  остальное — сканер и кэшированный snapshot).
- ADLX-сканирование синхронное и занимает 200–600 мс на типичной
  системе (1 GPU, 1 монитор). Запускается один раз при старте UI и по
  нажатию кнопки "Пересканировать".
- Никаких фоновых служб: приложение применяет профиль и выходит в CLI
  режиме; в GUI режиме мониторинг температур/частот опционален и
  опрашивает ADLX по таймеру 1 Гц только при открытой вкладке Performance.
