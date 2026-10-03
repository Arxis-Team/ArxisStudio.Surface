using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Ресурсы приложения документа, спрошенные через само приложение: словарь остаётся его.
/// </summary>
/// <remarks>
/// <para>
/// Словарь приложения нельзя перенести на форму, как переносится словарь окна. Ссылка
/// <c>{DynamicResource}</c>, написанная в разметке приложения, — в его теме окна, в его стилях — ищет
/// ключ от узла, в котором её написали: от самого приложения. Унесённый словарь оставил бы приложение
/// пустым, и такая ссылка не находила бы ничего — цвет текста формы приходил бы от инструмента.
/// </para>
/// <para>
/// Поэтому словарь остаётся владельцу, а области формы достаётся этот посредник: вопрос о ключе он
/// передаёт приложению, а о переменах в его ресурсах говорит своему хозяину. Хозяин у посредника один,
/// как у словаря, — им владеет база.
/// </para>
/// </remarks>
internal sealed class ApplicationResources(Application application) : ResourceProvider
{
    /// <inheritdoc />
    public override bool HasResources => true;

    /// <inheritdoc />
    public override bool TryGetResource(object key, ThemeVariant? theme, out object? value) =>
        application.TryGetResource(key, theme, out value);

    /// <inheritdoc />
    protected override void OnAddOwner(IResourceHost owner)
    {
        base.OnAddOwner(owner);
        application.ResourcesChanged += OnApplicationResourcesChanged;
    }

    /// <inheritdoc />
    protected override void OnRemoveOwner(IResourceHost owner)
    {
        application.ResourcesChanged -= OnApplicationResourcesChanged;
        base.OnRemoveOwner(owner);
    }

    private void OnApplicationResourcesChanged(object? sender, ResourcesChangedEventArgs e) =>
        Owner?.NotifyHostedResourcesChanged(e);
}
