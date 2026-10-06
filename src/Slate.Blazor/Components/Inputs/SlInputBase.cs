using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Slate.Blazor;

/// <summary>
/// Base for Slate inputs. Works standalone (<c>@bind-Value</c>) and inside an <c>EditForm</c>: when an
/// <see cref="EditContext"/> is cascaded and <see cref="ValueExpression"/> is known (it is with
/// <c>@bind-Value</c>), changes notify the context and its validation messages become the error text.
/// </summary>
public abstract class SlInputBase<TValue> : SlComponentBase, IDisposable
{
    private EditContext? _subscribedContext;

    [CascadingParameter] private EditContext? CascadedEditContext { get; set; }

    [Parameter] public TValue? Value { get; set; }
    [Parameter] public EventCallback<TValue?> ValueChanged { get; set; }
    [Parameter] public Expression<Func<TValue?>>? ValueExpression { get; set; }

    /// <summary>Visible label (always shown; never replaced by the placeholder).</summary>
    [Parameter] public string? Label { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>An explicit error. Takes precedence over EditContext validation messages.</summary>
    [Parameter] public string? Error { get; set; }

    protected EditContext? EditContext { get; private set; }

    protected FieldIdentifier? Field { get; private set; }

    /// <summary>Explicit <see cref="Error"/>, else the first validation message for this field.</summary>
    protected string? ErrorText =>
        !string.IsNullOrEmpty(Error) ? Error
        : EditContext is not null && Field is { } f ? EditContext.GetValidationMessages(f).FirstOrDefault()
        : null;

    protected bool IsInvalid => ErrorText is not null;

    protected TValue? CurrentValue
    {
        get => Value;
        set
        {
            if (EqualityComparer<TValue?>.Default.Equals(value, Value)) return;
            Value = value;
            _ = ValueChanged.InvokeAsync(value);
            if (EditContext is not null && Field is { } f)
                EditContext.NotifyFieldChanged(f);
        }
    }

    protected override void OnParametersSet()
    {
        EditContext = CascadedEditContext;
        Field = EditContext is not null && ValueExpression is not null ? FieldIdentifier.Create(ValueExpression) : null;

        if (!ReferenceEquals(_subscribedContext, EditContext))
        {
            if (_subscribedContext is not null) _subscribedContext.OnValidationStateChanged -= OnValidationStateChanged;
            _subscribedContext = EditContext;
            if (_subscribedContext is not null) _subscribedContext.OnValidationStateChanged += OnValidationStateChanged;
        }
    }

    private void OnValidationStateChanged(object? sender, ValidationStateChangedEventArgs e) => InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        if (_subscribedContext is not null) _subscribedContext.OnValidationStateChanged -= OnValidationStateChanged;
        GC.SuppressFinalize(this);
    }
}
