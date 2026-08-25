using System;

namespace AssettoServer.Server.Ai;

internal static class RaceBotDriverNames
{
    private static readonly string[] FirstNames =
    [
        "Max", "Lewis", "Lando", "Carlos",
        "Charles", "Fernando", "Kimi", "Niki",
        "Ayrton", "Nigel", "Damon", "Mario",
        "Mika", "Jenson", "Seb", "Valtteri"
    ];

    private static readonly string[] LastNames =
    [
        "Verboostin", "Hamsterwheel", "No-Risk", "Spinmaster",
        "LeClutch", "Allons-Y", "Brakeonen", "Louder",
        "Sendit", "Manslow", "Overhill", "Andready",
        "Hackalap", "Pushbutton", "Vettelfast", "Bottleneck"
    ];

    private const int NameCount = 256;
    private static readonly int ProcessOffset = Random.Shared.Next(NameCount);
    private static readonly int ProcessStep = Random.Shared.Next(NameCount / 2) * 2 + 1;

    internal static string Resolve(bool useParodyNames, string namePrefix, int sessionId) =>
        useParodyNames
            ? ForSlot(sessionId, ProcessOffset, ProcessStep)
            : $"{namePrefix} {sessionId}";

    internal static string ForSlot(int sessionId, int offset, int step)
    {
        // An odd step is coprime with 256, making this a permutation rather than
        // independent random picks. Every possible AC session slot is therefore unique.
        int index = (((sessionId & 0xFF) * (step | 1)) + (offset & 0xFF)) & 0xFF;
        return $"{FirstNames[index % FirstNames.Length]} {LastNames[index / FirstNames.Length]}";
    }
}
