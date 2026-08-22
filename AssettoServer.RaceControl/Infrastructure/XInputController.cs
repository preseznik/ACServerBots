using System.Runtime.InteropServices;

namespace AssettoServer.RaceControl.Infrastructure;

internal readonly record struct ControllerDrivingInput(float Steering, float Throttle, float Brake);

internal static class XInputController
{
    private const int LeftThumbDeadZone = 7849;
    private const byte TriggerThreshold = 30;
    private const ushort AButton = 0x1000;
    private const ushort BButton = 0x2000;

    public static bool TryRead(out ControllerDrivingInput input)
    {
        for (uint index = 0; index < 4; index++)
        {
            if (TryGetState(index, out var state) != 0)
                continue;
            input = new ControllerDrivingInput(
                NormalizeStick(state.Gamepad.ThumbLeftX),
                Math.Max(NormalizeTrigger(state.Gamepad.RightTrigger),
                    (state.Gamepad.Buttons & AButton) != 0 ? 1 : 0),
                Math.Max(NormalizeTrigger(state.Gamepad.LeftTrigger),
                    (state.Gamepad.Buttons & BButton) != 0 ? 1 : 0));
            return true;
        }
        input = default;
        return false;
    }

    private static uint TryGetState(uint index, out XInputState state)
    {
        try
        {
            return XInputGetState14(index, out state);
        }
        catch (DllNotFoundException)
        {
            try
            {
                return XInputGetState910(index, out state);
            }
            catch (DllNotFoundException)
            {
                state = default;
                return 1167;
            }
        }
        catch (EntryPointNotFoundException)
        {
            state = default;
            return 1167;
        }
    }

    private static float NormalizeStick(short value)
    {
        int magnitude = Math.Abs((int)value);
        if (magnitude <= LeftThumbDeadZone)
            return 0;
        return Math.Clamp(Math.Sign(value) * (magnitude - LeftThumbDeadZone)
                          / (float)(short.MaxValue - LeftThumbDeadZone), -1, 1);
    }

    private static float NormalizeTrigger(byte value) => value <= TriggerThreshold
        ? 0
        : (value - TriggerThreshold) / (float)(byte.MaxValue - TriggerThreshold);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState14(uint userIndex, out XInputState state);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState910(uint userIndex, out XInputState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLeftX;
        public short ThumbLeftY;
        public short ThumbRightX;
        public short ThumbRightY;
    }
}
