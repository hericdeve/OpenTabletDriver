using System;
using System.Collections.Generic;

class Program {
    static void Main() {
        var _shortcutButtonSlots = new Dictionary<ulong, int>()
        {
            { 0x050000000000, 0 }, // B
            { 0x080000000000, 1 }, // E
            { 0x0C0000000000, 2 }, // I
            { 0x1160000000000, 3 }, // Ctrl+S
            { 0x02C0000000000, 4 }, // Space
            { 0x51D0000000000, 5 }  // Ctrl+Alt+Z
        };
        Console.WriteLine($"Initialized dict size: {_shortcutButtonSlots.Count}");
    }
}
