namespace DiCAN.Core.Devices;

// Manages can chip.
public enum CanChipFamily
{

    Unknown = 0,

    Stm32F072,

    Stm32G431,
}

// Manages can host.
public enum CanHostPlatform
{

    Any = 0,

    Windows,

    MacOS,
}

// Manages can guidance.
public enum CanGuidanceClaim
{

    Ready,

    BootloaderUnplugTerminalBlock,

    BootloaderCheckBoot0Jumper,

    BootloaderDisableBoot0Permanently,

    BootloaderRescan,

    ReflashToSlcan25,

    F072CannotRunSlcan25,

    Legacy1ConfirmsNothing,

    Legacy1NoCanFd,

    NoPortCheckDriverBinding,

    NoSerialNodePublished,

    NothingFoundUsualCauses,
}

// Stores can guidance step data.
public readonly record struct CanGuidanceStep(
    CanGuidanceClaim Claim,
    CanChipFamily AppliesTo,
    CanHostPlatform Platform = CanHostPlatform.Any)
{

    public bool IsUniversal => AppliesTo == CanChipFamily.Unknown;
}

// Manages can device guidance.
public static class CanDeviceGuidance
{

    // Maps firmware to a model.
    public static IReadOnlyList<CanGuidanceStep> For(
        CanDeviceAdvice advice,
        CanChipFamily family = CanChipFamily.Unknown,
        CanHostPlatform platform = CanHostPlatform.Any)
    {
        IEnumerable<CanGuidanceStep> steps = AllSteps(advice);

        if (platform != CanHostPlatform.Any)
        {
            steps = steps.Where(s => s.Platform is CanHostPlatform.Any || s.Platform == platform);
        }

        if (family != CanChipFamily.Unknown)
        {
            steps = steps.Where(s => s.IsUniversal || s.AppliesTo == family);
        }

        return steps.ToArray();
    }

    public static CanHostPlatform CurrentPlatform =>
        OperatingSystem.IsWindows() ? CanHostPlatform.Windows
        : OperatingSystem.IsMacOS() ? CanHostPlatform.MacOS
        : CanHostPlatform.Any;

    // Gets needs chip.
    public static bool NeedsChipFamilyBranching(CanDeviceAdvice advice) =>
        AllSteps(advice).Any(s => !s.IsUniversal);

    // Gets all guidance steps.
    private static CanGuidanceStep[] AllSteps(CanDeviceAdvice advice) => advice switch
    {
        CanDeviceAdvice.None =>
        [
            new(CanGuidanceClaim.Ready, CanChipFamily.Unknown),
        ],

        CanDeviceAdvice.ExitBootloader =>
        [
            new(CanGuidanceClaim.BootloaderUnplugTerminalBlock, CanChipFamily.Stm32G431),
            new(CanGuidanceClaim.BootloaderCheckBoot0Jumper, CanChipFamily.Stm32F072),
            new(CanGuidanceClaim.BootloaderRescan, CanChipFamily.Unknown),
            new(CanGuidanceClaim.BootloaderDisableBoot0Permanently, CanChipFamily.Stm32G431),
        ],

        CanDeviceAdvice.ReplaceLegacyCanable1 =>
        [
            new(CanGuidanceClaim.Legacy1ConfirmsNothing, CanChipFamily.Unknown),
            new(CanGuidanceClaim.Legacy1NoCanFd, CanChipFamily.Unknown),
            new(CanGuidanceClaim.ReflashToSlcan25, CanChipFamily.Stm32G431),
            new(CanGuidanceClaim.F072CannotRunSlcan25, CanChipFamily.Stm32F072),
        ],

        CanDeviceAdvice.NoPortAssigned =>
        [
            new(CanGuidanceClaim.NoPortCheckDriverBinding, CanChipFamily.Unknown, CanHostPlatform.Windows),
            new(CanGuidanceClaim.NoSerialNodePublished, CanChipFamily.Unknown, CanHostPlatform.MacOS),
        ],

        CanDeviceAdvice.NothingFound =>
        [
            new(CanGuidanceClaim.NothingFoundUsualCauses, CanChipFamily.Unknown),
        ],

        _ => [],
    };
}
