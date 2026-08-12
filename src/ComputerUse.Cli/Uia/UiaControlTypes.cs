namespace ComputerUse.Cli.Uia;

internal static class UiaControlTypes
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [50000] = "Button",
        [50001] = "Calendar",
        [50002] = "CheckBox",
        [50003] = "ComboBox",
        [50004] = "Edit",
        [50005] = "Hyperlink",
        [50006] = "Image",
        [50007] = "ListItem",
        [50008] = "List",
        [50009] = "Menu",
        [50010] = "MenuBar",
        [50011] = "MenuItem",
        [50012] = "ProgressBar",
        [50013] = "RadioButton",
        [50014] = "ScrollBar",
        [50015] = "Slider",
        [50016] = "Spinner",
        [50017] = "StatusBar",
        [50018] = "Tab",
        [50019] = "TabItem",
        [50020] = "Text",
        [50021] = "ToolBar",
        [50022] = "ToolTip",
        [50023] = "Tree",
        [50024] = "TreeItem",
        [50025] = "Custom",
        [50026] = "Group",
        [50027] = "Thumb",
        [50028] = "DataGrid",
        [50029] = "DataItem",
        [50030] = "Document",
        [50031] = "SplitButton",
        [50032] = "Window",
        [50033] = "Pane",
        [50034] = "Header",
        [50035] = "HeaderItem",
        [50036] = "Table",
        [50037] = "TitleBar",
        [50038] = "Separator",
        [50039] = "SemanticZoom",
        [50040] = "AppBar",
    };

    public static string ToName(int controlTypeId)
        => Names.TryGetValue(controlTypeId, out var name) ? name : $"Unknown({controlTypeId})";

    /// <summary>Maps UIA ControlType IDs to Playwright-like ARIA role names.</summary>
    public static string ToAriaRole(int controlTypeId) => controlTypeId switch
    {
        50000 => "button",
        50001 => "calendar",
        50002 => "checkbox",
        50003 => "combobox",
        50004 => "textbox",
        50005 => "link",
        50006 => "img",
        50007 => "listitem",
        50008 => "list",
        50009 => "menu",
        50010 => "menubar",
        50011 => "menuitem",
        50012 => "progressbar",
        50013 => "radio",
        50014 => "scrollbar",
        50015 => "slider",
        50016 => "spinbutton",
        50017 => "statusbar",
        50018 => "tablist",
        50019 => "tab",
        50020 => "text",
        50021 => "toolbar",
        50022 => "tooltip",
        50023 => "tree",
        50024 => "treeitem",
        50025 => "generic",
        50026 => "group",
        50027 => "thumb",
        50028 => "grid",
        50029 => "gridcell",
        50030 => "document",
        50031 => "button",
        50032 => "window",
        50033 => "pane",
        50034 => "header",
        50035 => "columnheader",
        50036 => "table",
        50037 => "titlebar",
        50038 => "separator",
        50039 => "generic",
        50040 => "toolbar",
        _ => "generic",
    };

    public static bool IsTypicallyInteractive(int controlTypeId) => controlTypeId switch
    {
        50000 or // Button
        50002 or // CheckBox
        50003 or // ComboBox
        50004 or // Edit
        50005 or // Hyperlink
        50007 or // ListItem
        50011 or // MenuItem
        50013 or // RadioButton
        50015 or // Slider
        50016 or // Spinner
        50019 or // TabItem
        50024 or // TreeItem
        50029 or // DataItem
        50030 or // Document
        50031 => true, // SplitButton
        _ => false,
    };

    public static bool IsLayoutContainer(int controlTypeId) => controlTypeId switch
    {
        50025 or // Custom
        50026 or // Group
        50033 or // Pane
        50037 => true, // TitleBar (often chrome)
        _ => false,
    };
}
