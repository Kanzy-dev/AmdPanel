using System.Windows.Media;

namespace AmdPanel.App.ViewModels;

/// <summary>
/// Узел дерева навигации слева. Может быть либо группой (3D Settings,
/// Display, Drivers), либо конечной страницей (PageKey != null).
/// </summary>
public sealed class NavigationNode
{
    public string Key { get; init; } = string.Empty;        // ключ локализации
    public string? PageKey { get; init; }                    // null = group header
    public ImageSource? Icon { get; init; }
    public List<NavigationNode> Children { get; init; } = new();
    public bool IsGroup => PageKey is null;

    /// <summary> true для ноды, которая должна быть выбрана при первой отрисовке. </summary>
    public bool IsInitiallySelected { get; set; }
}
