using System;
using System.Collections.Generic;
using System.Globalization;
using ArxisStudio.Markup.Xaml.Loader;
using Avalonia;
using Avalonia.Media;

namespace UiDesigner.Demo.ViewModels;

/// <summary>How a value is edited, which follows from what kind of value it is.</summary>
public enum PropertyEditor
{
    /// <summary>Anything with no better answer.</summary>
    Text,

    /// <summary>A number, which is still text but is worth saying so.</summary>
    Number,

    /// <summary>A checkbox.</summary>
    Flag,

    /// <summary>A drop-down of the values the type allows.</summary>
    Choice,

    /// <summary>The same, laid out as one control per value, when there are few enough.</summary>
    Segmented,

    /// <summary>A colour, with the colour shown.</summary>
    Colour,

    /// <summary>A binding or other expression, which is shown and not edited as a value.</summary>
    Expression,
}

/// <summary>One value an enum allows, as the segmented control needs it.</summary>
/// <remarks>
/// An object rather than the bare string, because "is this the current one" is a question the
/// markup would otherwise have to answer by calling a method with an argument, which a compiled
/// binding cannot do. Here it is a property that raises a change, which is all a template needs.
/// </remarks>
public sealed class ChoiceOption : Observable
{
    private readonly PropertyRow _row;

    internal ChoiceOption(PropertyRow row, string text)
    {
        _row = row;
        Text = text;
        ChooseCommand = new RelayCommand(() => _row.Value = text);
    }

    public string Text { get; }

    public RelayCommand ChooseCommand { get; }

    public bool IsChosen => string.Equals(Text, _row.Value, StringComparison.OrdinalIgnoreCase);

    internal void Refresh() => Raise(nameof(IsChosen));
}

/// <summary>What a binding written on a property was found to read.</summary>
public enum BindingState
{
    /// <summary>The row holds no binding, or nothing has been asked about it.</summary>
    None,

    /// <summary>Every step of the binding's path is a member of the data it reads.</summary>
    Resolved,

    /// <summary>A step names nothing on the data — a member renamed or removed in the code.</summary>
    Broken,

    /// <summary>The binding reads something types cannot answer for, and is not checked.</summary>
    NotChecked,
}

/// <summary>A member of the data a property can be bound to, offered on the property's row.</summary>
/// <param name="Name">The member's name, as the binding's path writes it.</param>
/// <param name="TypeName">The member's type, as a reader writes it.</param>
/// <param name="Command">Writes the binding.</param>
public sealed record BindOption(string Name, string TypeName, RelayCommand Command);

/// <summary>A member of the data in scope, as the data section lists it.</summary>
/// <param name="Name">The member's name.</param>
/// <param name="TypeName">The member's type, as a reader writes it.</param>
/// <param name="Note">What else is worth knowing about it — writable, a collection, a command.</param>
public sealed record DataMember(string Name, string TypeName, string Note);

/// <summary>
/// One editable property of the selected element.
/// </summary>
/// <remarks>
/// <para>
/// A row is a fact about the <em>document</em>: the attribute's name as written, and its text. It is
/// deliberately not a fact about the live control, which would report the value every property has
/// rather than the ones this file sets — a Button has upwards of a hundred, three of which the
/// author wrote, and an inspector listing all of them tells nobody which ones matter.
/// </para>
/// <para>
/// How to edit it is the other question, and that one the document cannot answer: text is text. It
/// is answered by <see cref="XamlMemberDescriptor"/> — the value's type says whether this is a
/// checkbox, a list of the values an enum allows, or a colour — and by
/// <see cref="XamlValueInfo.HasBinding"/>, which is how a row knows it is showing an expression and
/// must not overwrite one with a literal.
/// </para>
/// </remarks>
public sealed class PropertyRow : Observable
{
    private readonly Func<string, string?> _validate;
    private readonly Action<PropertyRow, string> _commit;

    internal PropertyRow(
        string name,
        string value,
        bool isDirective,
        PropertyEditor editor,
        IReadOnlyList<string> choices,
        bool isReadOnly,
        Func<string, string?> validate,
        Action<PropertyRow, string> commit)
    {
        Name = name;
        IsDirective = isDirective;
        Editor = editor;
        Choices = choices;
        IsReadOnly = isReadOnly;

        _validate = validate;
        _commit = commit;
        _value = value;

        Options = [.. System.Linq.Enumerable.Select(choices, choice => new ChoiceOption(this, choice))];

        ResetCommand = new RelayCommand(() => Value = string.Empty, () => _value.Length > 0 && !IsReadOnly);
    }

    public string Name { get; }

    /// <summary>What the row is called on screen, when that is not what it is called in the file.</summary>
    /// <remarks>
    /// One name for one thing: <c>x:Name</c> is how a document names a control and "Name" is what a
    /// person calls it. The row writes the first and shows the second.
    /// </remarks>
    public string Label { get; internal set; } = string.Empty;

    /// <summary>The row's heading, which is its label when it has one and its name otherwise.</summary>
    public string Heading => Label is { Length: > 0 } ? Label : Name;

    /// <summary>Whether this is <c>x:Name</c> and its kind rather than an ordinary property.</summary>
    public bool IsDirective { get; }

    /// <summary>Which control edits it.</summary>
    public PropertyEditor Editor { get; }

    /// <summary>The values a choice allows, and nothing for every other kind.</summary>
    public IReadOnlyList<string> Choices { get; }

    /// <summary>The same, as the segmented control needs them.</summary>
    public IReadOnlyList<ChoiceOption> Options { get; }

    /// <summary>Whether the document may not write this — a read-only member, or an expression.</summary>
    public bool IsReadOnly { get; }

    public bool IsText => Editor is PropertyEditor.Text or PropertyEditor.Number;

    public bool IsFlag => Editor is PropertyEditor.Flag;

    public bool IsChoice => Editor is PropertyEditor.Choice;

    public bool IsSegmented => Editor is PropertyEditor.Segmented;

    public bool IsColour => Editor is PropertyEditor.Colour;

    public bool IsExpression => Editor is PropertyEditor.Expression;

    /// <summary>Whether this is a size the layout can decide instead: Width or Height.</summary>
    /// <remarks>
    /// Those two get a button beside the field that takes the number out. A control with a width
    /// of its own cannot stretch — the width wins over the alignment — and the way back to a size
    /// the layout decides is no size at all, which Avalonia spells <c>NaN</c>.
    /// </remarks>
    public bool IsSize { get; internal set; }

    /// <summary>What an empty field stands for, shown in place of the value it does not have.</summary>
    /// <remarks>
    /// <c>NaN</c> for a size the document does not set, and only when the live control agrees that
    /// is what it has. An empty Width field says nothing; a field reading NaN says the layout is
    /// sizing this, which is the thing a person pressing the reset wants to see happen.
    /// </remarks>
    public string Unset { get; internal set; } = string.Empty;

    /// <summary>Takes the value out of the document, which puts the property back to its default.</summary>
    public RelayCommand ResetCommand { get; }

    /// <summary>What the reset beside a size says it does.</summary>
    public string ResetTip =>
        $"Set {Name} back to NaN: the layout sizes the control, so its alignment can stretch";

    /// <summary>Whether this row takes the width of the panel rather than half of it.</summary>
    public bool IsWide => Editor is PropertyEditor.Segmented or PropertyEditor.Expression;

    /// <summary>The colour the value names, for the swatch beside it.</summary>
    public IBrush? Swatch
    {
        get;
        private set => Set(ref field, value);
    }

    /// <summary>What is wrong with the text, when something is.</summary>
    /// <remarks>
    /// Asked of the member rather than guessed at: <c>ConvertFromText</c> is the same conversion the
    /// load would perform, so a value this rejects is a value the document could not have meant. It
    /// is reported instead of written, because a designer that puts unparseable text in a file has
    /// broken the file on the user's behalf.
    /// </remarks>
    public string? Error
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    public bool HasError => Error is { Length: > 0 };

    /// <summary>
    /// The members of the data in scope this property can be bound to, filled once the data section
    /// has asked the session what the element's bindings read.
    /// </summary>
    /// <remarks>
    /// Names and a command each, never a type: a row lives as long as the selection does, and a
    /// <see cref="Type"/> held here would hold the generation of the project's code it came from.
    /// </remarks>
    public IReadOnlyList<BindOption> BindOptions
    {
        get;
        internal set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(CanBind));
            }
        }
    } = [];

    /// <summary>Whether the row offers to bind the property to a member of the data.</summary>
    public bool CanBind => BindOptions.Count > 0 && !IsReadOnly && !IsDirective && !IsExpression;

    /// <summary>What the binding this row shows was found to read.</summary>
    public BindingState Binding
    {
        get;
        internal set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(IsBindingBroken));
                Raise(nameof(IsBindingNotChecked));
            }
        }
    }

    /// <summary>Why the binding is broken or not checked, for the row's tooltip.</summary>
    public string? BindingMessage
    {
        get;
        internal set => Set(ref field, value);
    }

    public bool IsBindingBroken => Binding == BindingState.Broken;

    public bool IsBindingNotChecked => Binding == BindingState.NotChecked;

    private string _value;

    public string Value
    {
        get => _value;
        set
        {
            if (!Set(ref _value, value))
            {
                return;
            }

            Error = _validate(value);
            Swatch = BrushFor(value);

            foreach (ChoiceOption option in Options)
            {
                option.Refresh();
            }

            ResetCommand.RaiseCanExecuteChanged();

            if (!HasError)
            {
                _commit(this, value);
            }
        }
    }

    /// <summary>
    /// The same value as a colour, for the picker to edit.
    /// </summary>
    /// <remarks>
    /// Setting this does not write the document, which is the difference between this and
    /// <see cref="Value"/> and the reason it exists. A spectrum reports every colour the pointer
    /// crosses, and each write rebuilds the inspector — including the picker being dragged in, which
    /// would take its own flyout down with it on the first movement. So the picker moves the swatch
    /// and the text, and <see cref="CommitColour"/> writes the one colour the user stopped on.
    /// </remarks>
    public Color Colour
    {
        get => Color.TryParse(_value, out Color parsed) ? parsed : Colors.Transparent;
        set
        {
            string written = Text(value);

            if (string.Equals(written, _value, StringComparison.Ordinal))
            {
                return;
            }

            _value = written;

            Raise(nameof(Colour));
            Raise(nameof(Value));

            Error = _validate(written);
            Swatch = BrushFor(written);
        }
    }

    /// <summary>Writes the colour the picker was left on, once the picking is over.</summary>
    /// <remarks>
    /// A pick that changed nothing still arrives here, and costs nothing: the edit produces no
    /// change and the document is not replaced.
    /// </remarks>
    public void CommitColour()
    {
        if (!HasError)
        {
            _commit(this, _value);
        }
    }

    /// <summary>The colour as a document writes one, which drops the alpha when there is none.</summary>
    private static string Text(Color colour) => colour.A == 255
        ? string.Create(CultureInfo.InvariantCulture, $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}")
        : string.Create(CultureInfo.InvariantCulture, $"#{colour.A:X2}{colour.R:X2}{colour.G:X2}{colour.B:X2}");

    /// <summary>The flag's state, for a checkbox, which is the value read as a boolean.</summary>
    public bool Flag
    {
        get => bool.TryParse(_value, out bool parsed) && parsed;
        set => Value = value ? "True" : "False";
    }

    internal void Prime() => Swatch = BrushFor(_value);

    private static IBrush? BrushFor(string value)
    {
        if (value.Length == 0)
        {
            return null;
        }

        try
        {
            return Brush.Parse(value);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            // A colour written as something this cannot read is still a value the document may
            // legitimately hold -- a resource key, a binding's remains -- so the swatch simply has
            // nothing to show rather than the row having something to complain about.
            return null;
        }
    }

    internal static string Format(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>One of the inspector's headings, and the rows under it.</summary>
/// <remarks>
/// Split by width rather than by kind, because the design pairs the narrow editors two to a line and
/// gives the wide ones — a segmented alignment, an expression — the whole panel. Two lists is the
/// smallest thing that lays out both without a panel that has to measure its children to decide.
/// </remarks>
public sealed class PropertyGroup(string name, IReadOnlyList<PropertyRow> rows)
{
    public string Name { get; } = name;

    public IReadOnlyList<PropertyRow> Rows { get; } = rows;

    public IReadOnlyList<PropertyRow> Narrow { get; } = [.. System.Linq.Enumerable.Where(rows, r => !r.IsWide)];

    public IReadOnlyList<PropertyRow> Wide { get; } = [.. System.Linq.Enumerable.Where(rows, r => r.IsWide)];

    public bool HasNarrow => Narrow.Count > 0;

    public bool HasWide => Wide.Count > 0;
}
