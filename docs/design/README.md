# Alloy — design rules

Alloy is Slate's visual language: **precise, calm, built for the eight-hour day.** These documents are the
rules every Slate package implements. Values come from `design/tokens`; this folder explains how to use them.

1. [Principles](#principles)
2. [Colour](color.md)
3. [Typography, spacing & shape](foundations.md)
4. [Layout framework](layout.md)
5. [Component contracts](components.md)
6. [Systems: snackbars & dialogs](systems.md)

## Principles

1. **Crafted, not decorated.** Distinctiveness comes from detail, not ornament: the milled top edge on
   controls (`shadow.control` / `shadow.primary`), tabular numerals, hairline structure. No gradients as
   backgrounds, no glows, no illustrations in chrome.
2. **One accent, one meaning.** Teal (`color.accent.*`) means "act here". Mint (`color.focus.ring`,
   `color.selection.*`) means "you are here" and is never used for actions or text.
3. **Dense by default, never cramped.** Compact density (32px controls) is the default for productivity
   software; comfortable (40px) is opt-in per app or region. Nothing goes below 24px (WCAG 2.5.8).
4. **Keyboard first.** Every interactive element is reachable and operable by keyboard, has a visible
   focus ring, and exposes its shortcut where one exists (`Kbd`).
5. **Status is never colour alone.** Every severity pairs a hue with an icon and words.
6. **Depth means something.** Only floating layers (menus, dialogs, snackbars) cast real shadow
   (`shadow.e2`/`e3`). In dark mode, surfaces step *lighter* as they rise instead of relying on shadow.
7. **Platform-native behaviour, shared look.** A Slate button on WPF behaves like a WPF button (commands,
   access keys) and looks identical to the web one.

## Using the rules

- Reference **semantic** tokens (`color.text.secondary`), never palette tokens (`palette.gray.600`).
- Never hard-code a colour, size or duration in a component. If a value is missing, add a token.
- Contrast and parity rules are executable: `dotnet test tests/Slate.Tokens.Tests`.
