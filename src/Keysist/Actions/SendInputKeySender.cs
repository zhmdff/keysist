// src/Keysist/Actions/SendInputKeySender.cs
using System.Runtime.InteropServices;
using Keysist.Config;

namespace Keysist.Actions;

public class SendInputKeySender : IKeySender
{
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public KEYBDINPUT Ki;
        public ulong Padding; // pad union to MOUSEINPUT size
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKeyCode;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private const uint InputTypeKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numInputs, INPUT[] inputs, int sizeOfInputStruct);

    public void Send(IReadOnlyList<SendStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.DelayMs > 0) Thread.Sleep(step.DelayMs);

            if (step.Send == "vkey")
            {
                var vk = VirtualKeyNames.Resolve(step.Value);
                SendKeyDownUp(vk);
            }
            else if (step.Send == "text")
            {
                foreach (var ch in step.Value)
                    SendUnicodeChar(ch);
            }
        }
    }

    private void SendKeyDownUp(ushort vk)
    {
        var down = new INPUT { Type = InputTypeKeyboard, Ki = new KEYBDINPUT { VirtualKeyCode = vk } };
        var up = new INPUT { Type = InputTypeKeyboard, Ki = new KEYBDINPUT { VirtualKeyCode = vk, Flags = KeyEventFKeyUp } };
        SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
    }

    private void SendUnicodeChar(char ch)
    {
        var down = new INPUT { Type = InputTypeKeyboard, Ki = new KEYBDINPUT { ScanCode = ch, Flags = KeyEventFUnicode } };
        var up = new INPUT { Type = InputTypeKeyboard, Ki = new KEYBDINPUT { ScanCode = ch, Flags = KeyEventFUnicode | KeyEventFKeyUp } };
        SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
    }
}
