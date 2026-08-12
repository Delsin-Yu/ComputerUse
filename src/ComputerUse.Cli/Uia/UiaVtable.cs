using System.Runtime.InteropServices;
using ComputerUse.Cli.Win32;

namespace ComputerUse.Cli.Uia;

/// <summary>
/// Direct IUnknown-vtable calls into UIAutomation COM interfaces (Native AOT safe).
/// Slot numbers are 0-based COM method indices (after QueryInterface/AddRef/Release).
/// </summary>
internal static unsafe class UiaVtable
{
    // IUIAutomation
    private const int ElementFromHandleSlot = 3;
    private const int ElementFromPointSlot = 4;
    private const int GetFocusedElementSlot = 5;
    private const int ControlViewWalkerSlot = 11;
    private const int ContentViewWalkerSlot = 12;
    private const int RawViewWalkerSlot = 13;

    // IUIAutomationTreeWalker
    private const int GetFirstChildElementSlot = 1;
    private const int GetNextSiblingElementSlot = 3;

    // IUIAutomationElement
    private const int SetFocusSlot = 0;
    private const int GetCurrentPatternSlot = 13;
    private const int CurrentControlTypeSlot = 18;
    private const int CurrentNameSlot = 20;
    private const int CurrentHasKeyboardFocusSlot = 23;
    private const int CurrentIsKeyboardFocusableSlot = 24;
    private const int CurrentIsEnabledSlot = 25;
    private const int CurrentAutomationIdSlot = 26;
    private const int CurrentClassNameSlot = 27;
    private const int CurrentIsPasswordSlot = 32;
    private const int CurrentIsOffscreenSlot = 35;
    private const int CurrentBoundingRectangleSlot = 40;

    // Pattern IDs (UIAutomationClient.h)
    public const int InvokePatternId = 10000;
    public const int ValuePatternId = 10002;
    public const int ExpandCollapsePatternId = 10005;
    public const int SelectionItemPatternId = 10010;
    public const int TextPatternId = 10014;
    public const int TogglePatternId = 10015;

    // IUIAutomationInvokePattern
    private const int InvokeSlot = 0;

    // IUIAutomationValuePattern
    private const int ValueSetValueSlot = 0;
    private const int ValueGetCurrentValueSlot = 1;
    private const int ValueGetCurrentIsReadOnlySlot = 2;

    // IUIAutomationTogglePattern
    private const int ToggleSlot = 0;
    private const int ToggleGetCurrentToggleStateSlot = 1;

    // IUIAutomationSelectionItemPattern
    private const int SelectionItemSelectSlot = 0;
    private const int SelectionItemGetCurrentIsSelectedSlot = 3;

    // IUIAutomationExpandCollapsePattern
    private const int ExpandSlot = 0;
    private const int CollapseSlot = 1;
    private const int ExpandCollapseGetCurrentStateSlot = 2;

    // IUIAutomationTextPattern
    private const int TextGetDocumentRangeSlot = 4;

    // IUIAutomationTextRange
    private const int TextRangeGetTextSlot = 10;

    public static nint ElementFromHandle(nint automation, nint hwnd)
    {
        nint element;
        var hr = Call(automation, ElementFromHandleSlot, hwnd, &element);
        EnsureSuccess(hr, nameof(ElementFromHandle));
        return element;
    }

    public static nint ElementFromPoint(nint automation, int x, int y)
    {
        nint element;
        // POINT is two consecutive LONGS; on x64 it is passed by value in one 64-bit register.
        var packed = (long)(uint)x | ((long)(uint)y << 32);
        var hr = CallPackedPoint(automation, ElementFromPointSlot, packed, &element);
        EnsureSuccess(hr, nameof(ElementFromPoint));
        return element;
    }

    public static nint GetFocusedElement(nint automation)
    {
        nint element;
        var hr = Call(automation, GetFocusedElementSlot, &element);
        EnsureSuccess(hr, nameof(GetFocusedElement));
        return element;
    }

    public static nint GetControlViewWalker(nint automation)
    {
        nint walker;
        var hr = Call(automation, ControlViewWalkerSlot, &walker);
        EnsureSuccess(hr, nameof(GetControlViewWalker));
        return walker;
    }

    public static nint GetContentViewWalker(nint automation)
    {
        nint walker;
        var hr = Call(automation, ContentViewWalkerSlot, &walker);
        EnsureSuccess(hr, nameof(GetContentViewWalker));
        return walker;
    }

    public static nint GetRawViewWalker(nint automation)
    {
        nint walker;
        var hr = Call(automation, RawViewWalkerSlot, &walker);
        EnsureSuccess(hr, nameof(GetRawViewWalker));
        return walker;
    }

    public static nint GetFirstChildElement(nint walker, nint element)
    {
        nint child;
        var hr = Call(walker, GetFirstChildElementSlot, element, &child);
        if (hr == 1 /* S_FALSE */)
        {
            return nint.Zero;
        }

        EnsureSuccess(hr, nameof(GetFirstChildElement));
        return child;
    }

    public static nint GetNextSiblingElement(nint walker, nint element)
    {
        nint next;
        var hr = Call(walker, GetNextSiblingElementSlot, element, &next);
        if (hr == 1 /* S_FALSE */)
        {
            return nint.Zero;
        }

        EnsureSuccess(hr, nameof(GetNextSiblingElement));
        return next;
    }

    public static void SetFocus(nint element)
    {
        var hr = CallVoid(element, SetFocusSlot);
        EnsureSuccess(hr, nameof(SetFocus));
    }

    public static nint TryGetCurrentPattern(nint element, int patternId)
    {
        nint pattern;
        var hr = CallIntOutPtr(element, GetCurrentPatternSlot, patternId, &pattern);
        if (hr < 0 || pattern == nint.Zero)
        {
            return nint.Zero;
        }

        return pattern;
    }

    public static int GetCurrentControlType(nint element)
    {
        int value;
        var hr = Call(element, CurrentControlTypeSlot, &value);
        EnsureSuccess(hr, nameof(GetCurrentControlType));
        return value;
    }

    public static string GetCurrentName(nint element)
    {
        nint bstr;
        var hr = Call(element, CurrentNameSlot, &bstr);
        EnsureSuccess(hr, nameof(GetCurrentName));
        return UiaBstr.Read(bstr);
    }

    public static bool GetCurrentHasKeyboardFocus(nint element)
    {
        int value;
        var hr = Call(element, CurrentHasKeyboardFocusSlot, &value);
        EnsureSuccess(hr, nameof(GetCurrentHasKeyboardFocus));
        return value != 0;
    }

    public static bool GetCurrentIsKeyboardFocusable(nint element)
    {
        int value;
        var hr = Call(element, CurrentIsKeyboardFocusableSlot, &value);
        EnsureSuccess(hr, nameof(GetCurrentIsKeyboardFocusable));
        return value != 0;
    }

    public static string GetCurrentAutomationId(nint element)
    {
        nint bstr;
        var hr = Call(element, CurrentAutomationIdSlot, &bstr);
        EnsureSuccess(hr, nameof(GetCurrentAutomationId));
        return UiaBstr.Read(bstr);
    }

    public static string GetCurrentClassName(nint element)
    {
        nint bstr;
        var hr = Call(element, CurrentClassNameSlot, &bstr);
        EnsureSuccess(hr, nameof(GetCurrentClassName));
        return UiaBstr.Read(bstr);
    }

    public static bool GetCurrentIsEnabled(nint element)
    {
        int value;
        var hr = Call(element, CurrentIsEnabledSlot, &value);
        EnsureSuccess(hr, nameof(GetCurrentIsEnabled));
        return value != 0;
    }

    public static bool GetCurrentIsPassword(nint element)
    {
        int value;
        var hr = Call(element, CurrentIsPasswordSlot, &value);
        EnsureSuccess(hr, nameof(GetCurrentIsPassword));
        return value != 0;
    }

    public static bool GetCurrentIsOffscreen(nint element)
    {
        int value;
        var hr = Call(element, CurrentIsOffscreenSlot, &value);
        EnsureSuccess(hr, nameof(GetCurrentIsOffscreen));
        return value != 0;
    }

    public static NativeMethods.RECT GetCurrentBoundingRectangle(nint element)
    {
        NativeMethods.RECT value;
        var hr = Call(element, CurrentBoundingRectangleSlot, &value);
        EnsureSuccess(hr, nameof(GetCurrentBoundingRectangle));
        return value;
    }

    public static string? TryGetValuePatternValue(nint element, out bool isReadOnly)
    {
        isReadOnly = false;
        var pattern = TryGetCurrentPattern(element, ValuePatternId);
        if (pattern == nint.Zero)
        {
            return null;
        }

        try
        {
            nint bstr;
            var hr = Call(pattern, ValueGetCurrentValueSlot, &bstr);
            if (hr < 0)
            {
                return null;
            }

            var value = UiaBstr.Read(bstr);
            int readOnly;
            hr = Call(pattern, ValueGetCurrentIsReadOnlySlot, &readOnly);
            if (hr >= 0)
            {
                isReadOnly = readOnly != 0;
            }

            return value;
        }
        finally
        {
            UiaCom.Release(pattern);
        }
    }

    public static int? TryGetToggleState(nint element)
    {
        var pattern = TryGetCurrentPattern(element, TogglePatternId);
        if (pattern == nint.Zero)
        {
            return null;
        }

        try
        {
            int state;
            var hr = Call(pattern, ToggleGetCurrentToggleStateSlot, &state);
            return hr < 0 ? null : state;
        }
        finally
        {
            UiaCom.Release(pattern);
        }
    }

    public static bool? TryGetIsSelected(nint element)
    {
        var pattern = TryGetCurrentPattern(element, SelectionItemPatternId);
        if (pattern == nint.Zero)
        {
            return null;
        }

        try
        {
            int selected;
            var hr = Call(pattern, SelectionItemGetCurrentIsSelectedSlot, &selected);
            return hr < 0 ? null : selected != 0;
        }
        finally
        {
            UiaCom.Release(pattern);
        }
    }

    public static int? TryGetExpandCollapseState(nint element)
    {
        var pattern = TryGetCurrentPattern(element, ExpandCollapsePatternId);
        if (pattern == nint.Zero)
        {
            return null;
        }

        try
        {
            int state;
            var hr = Call(pattern, ExpandCollapseGetCurrentStateSlot, &state);
            return hr < 0 ? null : state;
        }
        finally
        {
            UiaCom.Release(pattern);
        }
    }

    public static bool HasInvokePattern(nint element)
    {
        var pattern = TryGetCurrentPattern(element, InvokePatternId);
        if (pattern == nint.Zero)
        {
            return false;
        }

        UiaCom.Release(pattern);
        return true;
    }

    public static void Invoke(nint element)
    {
        var pattern = TryGetCurrentPattern(element, InvokePatternId);
        if (pattern == nint.Zero)
        {
            throw new CliException("InvalidArgument", "Element does not support InvokePattern.", exitCode: 1);
        }

        try
        {
            EnsureSuccess(CallVoid(pattern, InvokeSlot), nameof(Invoke));
        }
        finally
        {
            UiaCom.Release(pattern);
        }
    }

    public static void SetValue(nint element, string value)
    {
        var pattern = TryGetCurrentPattern(element, ValuePatternId);
        if (pattern == nint.Zero)
        {
            throw new CliException("InvalidArgument", "Element does not support ValuePattern.", exitCode: 1);
        }

        nint bstr = nint.Zero;
        try
        {
            int readOnly;
            var hr = Call(pattern, ValueGetCurrentIsReadOnlySlot, &readOnly);
            if (hr >= 0 && readOnly != 0)
            {
                throw new CliException("InvalidArgument", "Element value is read-only.", exitCode: 1);
            }

            bstr = Marshal.StringToBSTR(value);
            EnsureSuccess(CallBstr(pattern, ValueSetValueSlot, bstr), nameof(SetValue));
        }
        finally
        {
            if (bstr != nint.Zero)
            {
                Marshal.FreeBSTR(bstr);
            }

            UiaCom.Release(pattern);
        }
    }

    public static void Toggle(nint element)
    {
        var pattern = TryGetCurrentPattern(element, TogglePatternId);
        if (pattern == nint.Zero)
        {
            throw new CliException("InvalidArgument", "Element does not support TogglePattern.", exitCode: 1);
        }

        try
        {
            EnsureSuccess(CallVoid(pattern, ToggleSlot), nameof(Toggle));
        }
        finally
        {
            UiaCom.Release(pattern);
        }
    }

    public static void Select(nint element)
    {
        var pattern = TryGetCurrentPattern(element, SelectionItemPatternId);
        if (pattern == nint.Zero)
        {
            throw new CliException("InvalidArgument", "Element does not support SelectionItemPattern.", exitCode: 1);
        }

        try
        {
            EnsureSuccess(CallVoid(pattern, SelectionItemSelectSlot), nameof(Select));
        }
        finally
        {
            UiaCom.Release(pattern);
        }
    }

    public static void Expand(nint element)
    {
        var pattern = TryGetCurrentPattern(element, ExpandCollapsePatternId);
        if (pattern == nint.Zero)
        {
            throw new CliException("InvalidArgument", "Element does not support ExpandCollapsePattern.", exitCode: 1);
        }

        try
        {
            EnsureSuccess(CallVoid(pattern, ExpandSlot), nameof(Expand));
        }
        finally
        {
            UiaCom.Release(pattern);
        }
    }

    public static void Collapse(nint element)
    {
        var pattern = TryGetCurrentPattern(element, ExpandCollapsePatternId);
        if (pattern == nint.Zero)
        {
            throw new CliException("InvalidArgument", "Element does not support ExpandCollapsePattern.", exitCode: 1);
        }

        try
        {
            EnsureSuccess(CallVoid(pattern, CollapseSlot), nameof(Collapse));
        }
        finally
        {
            UiaCom.Release(pattern);
        }
    }

    public static string? TryGetTextPatternValue(nint element, int maxLength)
    {
        var pattern = TryGetCurrentPattern(element, TextPatternId);
        if (pattern == nint.Zero)
        {
            return null;
        }

        nint range = nint.Zero;
        try
        {
            var hr = Call(pattern, TextGetDocumentRangeSlot, &range);
            if (hr < 0 || range == nint.Zero)
            {
                return null;
            }

            nint bstr;
            hr = CallIntOutBstr(range, TextRangeGetTextSlot, maxLength, &bstr);
            if (hr < 0)
            {
                return null;
            }

            return UiaBstr.Read(bstr);
        }
        finally
        {
            UiaCom.Release(range);
            UiaCom.Release(pattern);
        }
    }

    private static int Call(nint obj, int methodSlot, nint arg, nint* result)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)GetFunction(obj, methodSlot);
        return fn(obj, arg, result);
    }

    private static int CallPackedPoint(nint obj, int methodSlot, long packedPoint, nint* result)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, long, nint*, int>)GetFunction(obj, methodSlot);
        return fn(obj, packedPoint, result);
    }

    private static int Call(nint obj, int methodSlot, nint* result)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, nint*, int>)GetFunction(obj, methodSlot);
        return fn(obj, result);
    }

    private static int Call(nint obj, int methodSlot, int* result)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, int*, int>)GetFunction(obj, methodSlot);
        return fn(obj, result);
    }

    private static int Call(nint obj, int methodSlot, NativeMethods.RECT* result)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, NativeMethods.RECT*, int>)GetFunction(obj, methodSlot);
        return fn(obj, result);
    }

    private static int CallIntOutPtr(nint obj, int methodSlot, int arg, nint* result)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, int, nint*, int>)GetFunction(obj, methodSlot);
        return fn(obj, arg, result);
    }

    private static int CallIntOutBstr(nint obj, int methodSlot, int arg, nint* result)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, int, nint*, int>)GetFunction(obj, methodSlot);
        return fn(obj, arg, result);
    }

    private static int CallVoid(nint obj, int methodSlot)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, int>)GetFunction(obj, methodSlot);
        return fn(obj);
    }

    private static int CallBstr(nint obj, int methodSlot, nint bstr)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, int>)GetFunction(obj, methodSlot);
        return fn(obj, bstr);
    }

    private static void* GetFunction(nint obj, int methodSlot)
    {
        if (obj == nint.Zero)
        {
            throw new CliException("Win32Error", "Null COM object.", exitCode: 2);
        }

        // vtable[0..2] = IUnknown; COM method N is at vtable[N + 3]
        var vtable = *(void***)obj;
        return vtable[methodSlot + 3];
    }

    private static void EnsureSuccess(int hr, string api)
    {
        if (hr < 0)
        {
            throw new CliException("Win32Error", $"{api} failed (HRESULT 0x{hr:X8}).", exitCode: 2);
        }
    }
}
