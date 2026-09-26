using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ArxisStudio;
using CommunityToolkit.Mvvm.ComponentModel;
using ArxisStudio.Surface.UiDesigner;

namespace DesignEditor.Demo.ViewModels;

/// <summary>
/// Что описывает строка панели групп.
/// </summary>
public enum GroupNodeKind
{
    /// <summary>Форма — контейнер <see cref="DesignEditorItem"/>.</summary>
    Form,

    /// <summary>Группа внутри формы.</summary>
    Group,

    /// <summary>Участник группы.</summary>
    Member,

    /// <summary>Пояснение вместо содержимого: групп в форме нет.</summary>
    Hint
}

/// <summary>
/// Строка панели групп.
/// </summary>
/// <remarks>
/// Дерево разложено в плоский список: строка знает свой отступ и своё состояние
/// раскрытия, а панель просто не выпускает детей свёрнутого узла. Так исключён
/// двусторонний обмен выделением с <c>TreeView</c> — выделение течёт в одну сторону,
/// от редактора к панели, а клик идёт обратно вызовом публичного API.
/// </remarks>
public sealed partial class GroupNode : ObservableObject
{
    /// <summary>Инициализирует строку.</summary>
    public GroupNode(GroupNodeKind kind, string key, string title, int depth)
    {
        Kind = kind;
        Key = key;
        Title = title;
        Indent = new Thickness(8 + (depth * 16), 0, 0, 0);
    }

    /// <summary>Что описывает строка.</summary>
    public GroupNodeKind Kind { get; }

    /// <summary>
    /// Устойчивый ключ строки.
    /// </summary>
    /// <remarks>
    /// Панель пересобирается целиком на каждое событие редактора, и состояние
    /// раскрытия обязано это пережить — держать его в самой строке было бы всё равно
    /// что не держать вовсе.
    /// </remarks>
    public string Key { get; }

    /// <summary>Подпись строки.</summary>
    public string Title { get; }

    /// <summary>Отступ по уровню вложенности.</summary>
    public Thickness Indent { get; }

    /// <summary>Форма, к которой относится строка.</summary>
    public DesignEditorItem? Container { get; init; }

    /// <summary>Контрол строки — только у участника группы.</summary>
    public Control? Target { get; init; }

    /// <summary>
    /// Полный путь группы — у строки группы и у её участников.
    /// </summary>
    /// <remarks>
    /// Именно путь, а не имя уровня: обращаться к API нужно личностью группы, а имена
    /// уровней на разных ветках дерева могут совпадать. Показывается при этом
    /// <see cref="Title"/> — последний сегмент.
    /// </remarks>
    public string? GroupPath { get; init; }

    /// <summary>Число участников — у строки группы.</summary>
    [ObservableProperty]
    private int _memberCount;

    /// <summary>Признак того, что у строки есть дети.</summary>
    public bool HasChildren { get; init; }

    /// <summary>
    /// Цвет подписи по роли строки.
    /// </summary>
    /// <remarks>
    /// Группа выделена цветом намеренно: в списке она единственная строка, которой
    /// в дереве контролов ничего не соответствует — это пометка, а не узел.
    /// </remarks>
    public IBrush TitleBrush => Kind switch
    {
        GroupNodeKind.Form => FormBrush,
        GroupNodeKind.Group => GroupBrush,
        GroupNodeKind.Member => MemberBrush,
        _ => HintBrush
    };

    private static readonly IBrush FormBrush = new SolidColorBrush(Color.Parse("#E6E6E6"));
    private static readonly IBrush GroupBrush = new SolidColorBrush(Color.Parse("#E5A24B"));
    private static readonly IBrush MemberBrush = new SolidColorBrush(Color.Parse("#B7BCC5"));
    private static readonly IBrush HintBrush = new SolidColorBrush(Color.Parse("#6E7480"));

    /// <summary>Значок строки.</summary>
    public string Glyph => Kind switch
    {
        GroupNodeKind.Form => "▤",
        GroupNodeKind.Group => "⬚",
        GroupNodeKind.Member => "▭",
        _ => " "
    };

    /// <summary>Стрелка раскрытия; у строки без детей пусто.</summary>
    public string Chevron => !HasChildren ? " " : IsExpanded ? "⌄" : "›";

    /// <summary>Пояснение справа: число участников у группы.</summary>
    public string Note => Kind == GroupNodeKind.Group ? MemberCount.ToString() : string.Empty;

    /// <summary>Признак того, что строка выбрана в редакторе.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Признак раскрытого узла.</summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(Chevron));

    partial void OnMemberCountChanged(int value) => OnPropertyChanged(nameof(Note));

    /// <summary>Признак того, что имя группы правится на месте.</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>Правимое имя группы.</summary>
    [ObservableProperty]
    private string _editText = string.Empty;

    /// <summary>Признак обычного показа: подпись видна, поле ввода нет.</summary>
    public bool IsNotEditing => !IsEditing;

    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(IsNotEditing));
}
