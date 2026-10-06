using System.Windows;
using System.Windows.Markup;

[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

// One XAML namespace for everything Slate: xmlns:sl="https://slate.dev/wpf"
[assembly: XmlnsDefinition("https://slate.dev/wpf", "Slate.Wpf")]
[assembly: XmlnsDefinition("https://slate.dev/wpf", "Slate", AssemblyName = "Slate.Core")]
[assembly: XmlnsDefinition("https://slate.dev/wpf", "Slate.Dialogs", AssemblyName = "Slate.Core")]
[assembly: XmlnsDefinition("https://slate.dev/wpf", "Slate.Snackbars", AssemblyName = "Slate.Core")]
[assembly: XmlnsDefinition("https://slate.dev/wpf", "Slate.Layout", AssemblyName = "Slate.Core")]
[assembly: XmlnsDefinition("https://slate.dev/wpf", "Slate.Dates", AssemblyName = "Slate.Core")]
[assembly: XmlnsPrefix("https://slate.dev/wpf", "sl")]
