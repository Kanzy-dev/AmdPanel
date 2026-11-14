using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AmdPanel.App.Mvvm;

/// <summary>
/// Базовый класс для всех ViewModel. Реализует INotifyPropertyChanged и
/// инфраструктуру Dirty State (IsDirty), которая активирует кнопки
/// "Применить" / "Отмена" в MainWindow только когда пользователь реально
/// изменил какой-то параметр.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    private bool _isDirty;

    /// <summary> true, если есть несохранённые изменения в текущей VM. </summary>
    public bool IsDirty
    {
        get => _isDirty;
        protected set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            OnPropertyChanged();
            DirtyStateChanged?.Invoke(this, _isDirty);
        }
    }

    /// <summary>
    /// Событие поднимается при изменении IsDirty. MainWindow подписывается
    /// и включает/выключает кнопки Apply / Cancel.
    /// </summary>
    public event EventHandler<bool>? DirtyStateChanged;

    /// <summary> Сброс Dirty (после Apply или Cancel). </summary>
    public void ClearDirty()
    {
        IsDirty = false;
        OnDirtyCleared();
    }

    /// <summary> Hook для наследников: выполнить очистку локальных undo-буферов. </summary>
    protected virtual void OnDirtyCleared() { }

    /// <summary>
    /// Помечает свойство как изменённое + поднимает PropertyChanged.
    /// </summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        IsDirty = true;
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Marker-интерфейс для page view models, чтобы MainWindow мог найти
/// соответствующий View по ViewModel через DataTemplate.
/// </summary>
public interface IPageViewModel
{
    string PageKey { get; }       // уникальный ключ для локализации заголовка
    bool IsAvailable { get; }     // доступность страницы (зависит от железа)
}
