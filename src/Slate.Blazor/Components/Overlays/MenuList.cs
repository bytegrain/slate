using Microsoft.AspNetCore.Components.Web;
using Slate.Blazor.Internal;
using Slate.Collections;

namespace Slate.Blazor;

/// <summary>
/// The items of one menu panel (a menu or a submenu): registration in render order, roving focus with wrapping,
/// typeahead, and closing the whole menu tree. Shared by <see cref="SlMenu"/> and <see cref="SlMenuItem"/>.
/// </summary>
public sealed class MenuList
{
    private readonly List<SlMenuItem> _items = [];
    private readonly Typeahead _typeahead = new();
    private readonly SlateJs _js;
    private readonly TimeProvider _time;

    internal MenuList(SlMenu root, SlMenuItem? owner, SlateJs js, TimeProvider time)
    {
        Root = root;
        Owner = owner;
        _js = js;
        _time = time;
    }

    /// <summary>The top-level menu this list belongs to.</summary>
    public SlMenu Root { get; }

    /// <summary>The item that owns this list when it is a submenu; null for the top level.</summary>
    public SlMenuItem? Owner { get; }

    public IReadOnlyList<SlMenuItem> Items => _items;

    /// <summary>Index of the item that has focus, or -1.</summary>
    public int FocusedIndex { get; internal set; } = -1;

    internal void Register(SlMenuItem item)
    {
        if (!_items.Contains(item)) _items.Add(item);
    }

    internal void Unregister(SlMenuItem item) => _items.Remove(item);

    private IReadOnlyList<bool> Skips => _items.Select(i => i.Disabled || i.Separator).ToList();

    internal Task FocusAsync(int index)
    {
        if (index < 0 || index >= _items.Count) return Task.CompletedTask;
        FocusedIndex = index;
        return JsSafe.Run(() => _js.FocusIdAsync(_items[index].ItemId)).AsTask();
    }

    internal Task FocusFirstAsync() => FocusAsync(ListNavigator.Move(-1, ListKey.First, Skips));

    internal Task FocusLastAsync() => FocusAsync(ListNavigator.Move(-1, ListKey.Last, Skips));

    /// <summary>Handles a key inside this panel. Returns true when used.</summary>
    internal async Task<bool> HandleKeyAsync(KeyboardEventArgs e)
    {
        ListKey? key = e.Key switch
        {
            "ArrowDown" => ListKey.Next,
            "ArrowUp" => ListKey.Previous,
            "Home" => ListKey.First,
            "End" => ListKey.Last,
            _ => null,
        };
        if (key is { } k)
        {
            await FocusAsync(ListNavigator.Move(FocusedIndex, k, Skips, new ListNavigationOptions { Wrap = true }));
            return true;
        }

        var focused = FocusedIndex >= 0 && FocusedIndex < _items.Count ? _items[FocusedIndex] : null;
        switch (e.Key)
        {
            case "Enter":
            case " ":
                if (focused is not null) await focused.ActivateAsync(fromKeyboard: true);
                return true;
            case "ArrowRight":
                if (focused is { HasSubmenu: true }) await focused.OpenSubmenuAsync(focusFirst: true);
                return true;
            case "ArrowLeft":
                if (Owner is not null) await Owner.CloseSubmenuAsync(refocus: true);
                return true;
            case "Tab":
                await Root.CloseAsync(returnFocus: false);
                return false;
        }

        if (e.Key.Length == 1 && !e.CtrlKey && !e.MetaKey && !e.AltKey)
        {
            var found = _typeahead.Search(e.Key, _time.GetUtcNow().ToUnixTimeMilliseconds(), _items.Select(i => i.Text).ToList(), FocusedIndex, Skips);
            if (found >= 0) await FocusAsync(found);
            return true;
        }
        return false;
    }

    /// <summary>Closes every open submenu in this list (and below).</summary>
    internal async Task CloseSubmenusAsync(SlMenuItem? except = null)
    {
        foreach (var item in _items.ToList())
            if (!ReferenceEquals(item, except)) await item.CloseSubmenuAsync(refocus: false);
    }
}
