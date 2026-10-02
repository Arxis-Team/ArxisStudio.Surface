using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using Avalonia.Controls;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// The inspector's data section: what the selected element's bindings read, and binding to it.
/// </summary>
/// <remarks>
/// <para>
/// Every answer comes from Markup, by name (its ADR 0026): the data type in scope and the design
/// data's type from <see cref="XamlLoadSession.GetDataContextAsync"/>, the members a binding can name
/// from <see cref="XamlMemberResolver.EnumerateBindable"/>, and whether a written path still resolves
/// from <see cref="XamlMemberResolver.ResolveBindingPath"/>. The types those answers come with are
/// read here and let go: what the section and the rows keep is names and commands, because a type
/// held by the inspector would hold the generation of the project's code it came from.
/// </para>
/// <para>
/// A binding is written as text through the form's document, like every edit. Where the project
/// compiles bindings and nothing in scope names a data type, the section offers nothing to bind to:
/// a compiled binding with no data type is a form that does not load.
/// </para>
/// </remarks>
public sealed partial class DesignerViewModel
{
    /// <summary>What the data section says about the data type in scope.</summary>
    public string DataTypeText
    {
        get;
        private set => Set(ref field, value);
    } = string.Empty;

    /// <summary>What the data section says about the design data.</summary>
    public string DesignDataText
    {
        get;
        private set => Set(ref field, value);
    } = string.Empty;

    /// <summary>Whether the data section has anything to say about the selection.</summary>
    public bool HasDataSection
    {
        get;
        private set => Set(ref field, value);
    }

    /// <summary>The members of the data in scope.</summary>
    public ObservableCollection<DataMember> DataMembers { get; } = [];

    /// <summary>Whether the data in scope has members to list.</summary>
    public bool HasDataMembers
    {
        get;
        private set => Set(ref field, value);
    }

    /// <summary>Writes <c>Design.DataContext</c> with an instance of the data type in scope.</summary>
    public RelayCommand CreateDesignDataCommand { get; private set; } = null!;

    /// <summary>
    /// Whether the data type in scope resolves, can be made with nothing, and has no design data yet.
    /// </summary>
    public bool CanCreateDesignData
    {
        get;
        private set => Set(ref field, value);
    }

    /// <summary>Where design data would be written, and the type name it would be written with.</summary>
    private (XamlElementPath Scope, string TypeName)? _designDataTarget;

    /// <summary>Which selection the data section was last filled for, so a late answer for another is dropped.</summary>
    private XamlElement? _dataFor;

    private void InitialiseData() =>
        CreateDesignDataCommand = new RelayCommand(
            () => Run(CreateDesignDataAsync),
            () => _designDataTarget is not null && ActiveForm?.Live is not null);

    /// <summary>Empties the data section, for a selection it has nothing to say about.</summary>
    private void ClearData()
    {
        _dataFor = null;
        _designDataTarget = null;

        HasDataSection = false;
        CanCreateDesignData = false;
        DataTypeText = string.Empty;
        DesignDataText = string.Empty;
        DataMembers.Clear();
        HasDataMembers = false;

        CreateDesignDataCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Asks the form's session what the element's bindings read, and fills the section and the rows.
    /// </summary>
    /// <remarks>
    /// After the rows are built, because the answer is asynchronous: the data type is resolved
    /// through the environment, and the design data is read on the owning thread. The rows the
    /// answer is for are the ones current when it arrives — an inspector rebuilt in the meantime is a
    /// different selection or a different text, and the answer is dropped.
    /// </remarks>
    private async Task FillDataAsync(FormViewModel form, XamlElement element)
    {
        _dataFor = element;

        // A form behind its text shows objects of an earlier document, and this element is of the
        // current one: there is nothing to ask the session about it.
        if (form.Live is not { State: XamlLiveDocumentState.Live } live
            || live.Session is not { } session
            || !ReferenceEquals(element.Document, session.Document))
        {
            ClearData();

            return;
        }

        XamlDataContextInfo info = await session.GetDataContextAsync(element, _shutdown.Token);

        if (!ReferenceEquals(_dataFor, element) || !ReferenceEquals(Selected, element))
        {
            return;
        }

        XamlMemberResolver members = session.Environment.MemberResolver;
        Type? source = info.DataType ?? info.DesignDataContextType;

        HasDataSection = true;

        DataTypeText = info.WrittenDataType is { } written && info.DataTypeElement is { } declaring
            ? $"{written}  ·  on {declaring.Name}" + (info.DataType is null ? "  ·  does not resolve" : string.Empty)
            : "none in scope";

        DesignDataText = info.DesignDataContextType?.Name ?? "none";

        _designDataTarget = info is { DataType: { IsAbstract: false } type, DesignDataContextType: null, DataTypeElement: { } scope }
            && type.GetConstructor(Type.EmptyTypes) is not null
            && TypeNameIn(info.WrittenDataType!) is { } name
            && !scope.IsPropertyElementSyntax
                ? (XamlElementPath.Of(scope), name)
                : null;

        CanCreateDesignData = _designDataTarget is not null;
        CreateDesignDataCommand.RaiseCanExecuteChanged();

        DataMembers.Clear();

        var bindable = source is null ? [] : members.EnumerateBindable(source);

        foreach (XamlBindableMember member in bindable)
        {
            DataMembers.Add(new DataMember(member.Name, member.TypeName, Note(member)));
        }

        HasDataMembers = DataMembers.Count > 0;

        // Offered only where a binding would load: compiled bindings need the data type written.
        bool canBind = source is not null && (info.DataType is not null || !info.CompilesBindings);

        // What each member holds, read now and let go when this method returns: a row keeps names.
        Dictionary<string, Type?> held = source is null
            ? []
            : bindable.ToDictionary(
                static member => member.Name,
                member => members.ResolveBindingPath(source, member.Name).ResultType,
                StringComparer.Ordinal);

        Control? shown = LiveSelection();

        foreach (PropertyRow row in Properties)
        {
            if (row.IsExpression)
            {
                (row.Binding, row.BindingMessage) = source is null
                    ? (BindingState.NotChecked, "No data type or design data is in scope to read the path against.")
                    : Check(row.Value, source, members);

                continue;
            }

            if (!canBind || row.IsDirective || row.IsReadOnly)
            {
                row.BindOptions = [];

                continue;
            }

            Type? target = shown is not null && session.GetMember(shown, row.Name) is { IsResolved: true } descriptor
                ? descriptor.ValueType
                : null;

            row.BindOptions =
            [
                .. bindable
                    .Where(member => Accepts(target, held.GetValueOrDefault(member.Name)))
                    .Select(member => new BindOption(
                        member.Name,
                        member.TypeName,
                        new RelayCommand(() => Run(() => SetPropertyAsync(element, row.Name, $"{{Binding {member.Name}}}"))))),
            ];
        }

        static string Note(XamlBindableMember member) =>
            string.Join(
                ", ",
                new[]
                {
                    member.CanWrite ? "writable" : null,
                    member.IsCollection ? "collection" : null,
                    member.IsCommand ? "command" : null,
                }.OfType<string>());
    }

    /// <summary>Whether a member's value is worth offering to a property of a type.</summary>
    /// <remarks>
    /// Text takes anything, because a binding turns it into text; otherwise the member's type has to
    /// be the property's or convert as a binding converts numbers. A property whose type cannot be
    /// asked is offered everything rather than nothing.
    /// </remarks>
    private static bool Accepts(Type? property, Type? member)
    {
        if (property is null || property == typeof(string) || property == typeof(object))
        {
            return true;
        }

        if (member is null)
        {
            return false;
        }

        Type wanted = Nullable.GetUnderlyingType(property) ?? property;
        Type held = Nullable.GetUnderlyingType(member) ?? member;

        return wanted.IsAssignableFrom(held)
            || (Numeric(wanted) && Numeric(held));

        static bool Numeric(Type type) =>
            Type.GetTypeCode(type) is >= TypeCode.SByte and <= TypeCode.Decimal && !type.IsEnum;
    }

    /// <summary>
    /// Reads a written binding's path against the data it reads, when it is a binding to the data
    /// context at all.
    /// </summary>
    private static (BindingState State, string? Message) Check(string written, Type source, XamlMemberResolver members)
    {
        if (XamlValue.Parse(written) is not XamlMarkupExtensionValue { TypeName.LocalName: "Binding" or "CompiledBinding" or "ReflectionBinding" } binding)
        {
            return (BindingState.None, null);
        }

        // A binding to an element, an ancestor or a source of its own reads something else.
        if (binding.GetArgument("ElementName") is not null
            || binding.GetArgument("RelativeSource") is not null
            || binding.GetArgument("Source") is not null)
        {
            return (BindingState.NotChecked, "The binding reads something other than the data context.");
        }

        string path = (binding.PositionalArguments.FirstOrDefault() ?? binding.GetArgument("Path"))?.Value
            is XamlLiteralValue { Text: { } text }
                ? text
                : string.Empty;

        XamlBindingPathResult result = members.ResolveBindingPath(source, path);

        return result.Status switch
        {
            XamlBindingPathStatus.Resolved => (BindingState.Resolved, null),
            XamlBindingPathStatus.Broken => (BindingState.Broken, result.Message),
            _ => (BindingState.NotChecked, result.Message),
        };
    }

    /// <summary>The type name an <c>x:DataType</c> is written with, without <c>{x:Type}</c> around it.</summary>
    private static string? TypeNameIn(string written)
    {
        if (XamlValue.Parse(written.Trim()) is XamlMarkupExtensionValue extension)
        {
            return (extension.PositionalArguments.FirstOrDefault() ?? extension.GetArgument("TypeName"))?.Value
                is XamlLiteralValue { Text: { } argument }
                    ? argument.Trim()
                    : null;
        }

        return written.Trim() is { Length: > 0 } name ? name : null;
    }

    /// <summary>
    /// Writes <c>Design.DataContext</c> on the element that declares the data type, with an instance of
    /// it — written with the name the document already names the type by.
    /// </summary>
    private async Task CreateDesignDataAsync()
    {
        if (ActiveForm is not { } form || _designDataTarget is not { } target)
        {
            return;
        }

        await ApplyAsync(
            form,
            editor =>
            {
                if (target.Scope.Resolve(editor.Document) is not { } scope)
                {
                    return;
                }

                XamlQualifiedName member = editor.Qualify(scope, AvaloniaNamespace, "Design.DataContext");

                editor.SetPropertyElement(scope, member, $"<{target.TypeName} />");
            },
            "create design data");

        Log($"  design data written: {target.TypeName}");
    }

    /// <summary>Avalonia's XAML namespace, which <c>Design.DataContext</c> is written in.</summary>
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
}
