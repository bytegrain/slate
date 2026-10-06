using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Slate.Blazor;

/// <summary>
/// Shared input plumbing. Works standalone (<c>@bind-…</c>) and inside an <c>EditForm</c>: when an
/// <see cref="EditContext"/> is cascaded and the bound expression is known, changes notify the context and its
/// validation messages become the error text. Derived classes expose the bound parameter under the canonical name
/// (<c>Value</c> for text/radio, <c>Checked</c> for checkbox/switch).
/// </summary>
public abstract class SlFormComponentBase<TValue> : SlComponentBase, IDisposable
{
    private EditContext? _subscribedContext;

    [CascadingParameter] private EditContext? CascadedEditContext { get; set; }

    /// <summary>Visible label (always shown; never replaced by the placeholder).</summary>
    [Parameter] public string? Label { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>An explicit error. Takes precedence over EditContext validation messages.</summary>
    [Parameter] public string? Error { get; set; }

    protected abstract TValue? BoundValue { get; set; }
    protected abstract EventCallback<TValue?> BoundValueChanged { get; }
    protected abstract Expression<Func<TValue?>>? BoundExpression { get; }

    protected EditContext? EditContext { get; private set; }

    protected FieldIdentifier? Field { get; private set; }

    /// <summary>Explicit <see cref="Error"/>, else the first validation message for this field.</summary>
    protected virtual string? ErrorText =>
        !string.IsNullOrEmpty(Error) ? Error
        : EditContext is not null && Field is { } f ? EditContext.GetValidationMessages(f).FirstOrDefault()
        : null;

    protected bool IsInvalid => ErrorText is not null;

    protected TValue? CurrentValue
    {
        get => BoundValue;
        set
        {
            if (EqualityComparer<TValue?>.Default.Equals(value, BoundValue)) return;
            BoundValue = value;
            _ = BoundValueChanged.InvokeAsync(value);
            if (EditContext is not null && Field is { } f)
                EditContext.NotifyFieldChanged(f);
        }
    }

    protected override void OnParametersSet()
    {
        EditContext = CascadedEditContext;
        Field = EditContext is not null && BoundExpression is not null ? FieldIdentifier.Create(BoundExpression) : null;

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

/// <summary>Inputs bound through <c>Value</c> / <c>ValueChanged</c> (<c>@bind-Value</c>).</summary>
public abstract class SlInputBase<TValue> : SlFormComponentBase<TValue>
{
    [Parameter] public TValue? Value { get; set; }
    [Parameter] public EventCallback<TValue?> ValueChanged { get; set; }
    [Parameter] public Expression<Func<TValue?>>? ValueExpression { get; set; }

    protected override TValue? BoundValue { get => Value; set => Value = value; }
    protected override EventCallback<TValue?> BoundValueChanged => ValueChanged;
    protected override Expression<Func<TValue?>>? BoundExpression => ValueExpression;
}

/// <summary>Toggles bound through <c>Checked</c> / <c>CheckedChanged</c> (<c>@bind-Checked</c>).</summary>
public abstract class SlCheckedBase<TValue> : SlFormComponentBase<TValue>
{
    [Parameter] public TValue? Checked { get; set; }
    [Parameter] public EventCallback<TValue?> CheckedChanged { get; set; }
    [Parameter] public Expression<Func<TValue?>>? CheckedExpression { get; set; }

    protected override TValue? BoundValue { get => Checked; set => Checked = value; }
    protected override EventCallback<TValue?> BoundValueChanged => CheckedChanged;
    protected override Expression<Func<TValue?>>? BoundExpression => CheckedExpression;
}
