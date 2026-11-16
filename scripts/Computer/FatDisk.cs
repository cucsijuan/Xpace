using System;
using System.Text;

namespace Xpace.Computer;

/// <summary>
/// Files in the root directory of a FAT12 diskette image: a blank data diskette
/// with files written on it (a flight recorder's), or tests that put a program's
/// source on the ship's diskette and look at what COS wrote.
/// Names are as COS writes them: "COOL.ASM".
/// </summary>
public static class FatDisk
{
    private readonly record struct Layout(int Sector, int Cluster, int Fat, int Fats, int FatBytes, int Root, int Data, int Clusters);

    private static Layout Of(byte[] disk)
    {
        int sector = disk[11] | disk[12] << 8, perCluster = disk[13], reserved = disk[14] | disk[15] << 8;
        int fats = disk[16], rootEntries = disk[17] | disk[18] << 8, perFat = disk[22] | disk[23] << 8;
        int sectors = disk[19] | disk[20] << 8;
        int fat = reserved * sector, root = fat + fats * perFat * sector, data = root + rootEntries * 32;
        return new Layout(sector, perCluster * sector, fat, fats, perFat * sector, root, data,
                          (sectors * sector - data) / (perCluster * sector) + 2);
    }

    /// <summary>The 11 characters of a directory entry: "COOL    ASM".</summary>
    private static string EntryName(string name)
    {
        string[] parts = name.ToUpperInvariant().Split('.');
        return parts[0].PadRight(8)[..8] + (parts.Length > 1 ? parts[1] : "").PadRight(3)[..3];
    }

    private static int Next(byte[] disk, Layout l, int cluster)
    {
        int f = l.Fat + cluster * 3 / 2;
        int entry = disk[f] | disk[f + 1] << 8;
        return (cluster & 1) != 0 ? entry >> 4 : entry & 0xFFF;
    }

    private static void SetNext(byte[] disk, Layout l, int cluster, int next)
    {
        for (int copy = 0; copy < l.Fats; copy++)
        {
            int f = l.Fat + copy * l.FatBytes + cluster * 3 / 2;
            if ((cluster & 1) != 0)
            {
                disk[f] = (byte)(disk[f] & 0x0F | next << 4);
                disk[f + 1] = (byte)(next >> 4);
            }
            else
            {
                disk[f] = (byte)next;
                disk[f + 1] = (byte)(disk[f + 1] & 0xF0 | next >> 8 & 0x0F);
            }
        }
    }

    private static int Find(byte[] disk, Layout l, string entry)
    {
        for (int e = l.Root; e < l.Data && disk[e] != 0; e += 32)
            if (disk[e] != 0xE5 && Encoding.ASCII.GetString(disk, e, 11) == entry)
                return e;
        return -1;
    }

    /// <summary>The file's bytes; null if it is not there, or not all written yet.</summary>
    public static byte[] Read(byte[] disk, string name)
    {
        Layout l = Of(disk);
        int e = Find(disk, l, EntryName(name));
        if (e < 0)
            return null;
        int size = BitConverter.ToInt32(disk, e + 28), cluster = disk[e + 26] | disk[e + 27] << 8;
        var bytes = new byte[size];
        for (int done = 0; done < size; done += l.Cluster)
        {
            if (cluster < 2 || cluster >= l.Clusters)
                return null;
            Array.Copy(disk, l.Data + (cluster - 2) * l.Cluster, bytes, done, Math.Min(l.Cluster, size - done));
            cluster = Next(disk, l, cluster);
        }
        return bytes;
    }

    /// <summary>Writes the file, replacing one of the same name. False if the diskette is full.</summary>
    public static bool Write(byte[] disk, string name, byte[] bytes)
    {
        Layout l = Of(disk);
        string entry = EntryName(name);
        int e = Find(disk, l, entry);
        if (e >= 0)
        {
            for (int c = disk[e + 26] | disk[e + 27] << 8; c >= 2 && c < l.Clusters;)
            {
                int next = Next(disk, l, c);
                SetNext(disk, l, c, 0);
                c = next;
            }
        }
        else
        {
            for (e = l.Root; e < l.Data && disk[e] != 0 && disk[e] != 0xE5; e += 32) { }
            if (e >= l.Data)
                return false;
        }
        int first = 0, last = 0;
        for (int done = 0, c = 2; done < bytes.Length; done += l.Cluster)
        {
            while (c < l.Clusters && Next(disk, l, c) != 0)
                c++;
            if (c >= l.Clusters)
                return false;
            Array.Clear(disk, l.Data + (c - 2) * l.Cluster, l.Cluster);
            Array.Copy(bytes, done, disk, l.Data + (c - 2) * l.Cluster, Math.Min(l.Cluster, bytes.Length - done));
            if (last != 0)
                SetNext(disk, l, last, c);
            else
                first = c;
            SetNext(disk, l, c, 0xFFF);
            last = c;
        }
        Array.Clear(disk, e, 32);
        Encoding.ASCII.GetBytes(entry, 0, 11, disk, e);
        disk[e + 11] = 0x20;                    // archive
        disk[e + 26] = (byte)first;
        disk[e + 27] = (byte)(first >> 8);
        BitConverter.GetBytes(bytes.Length).CopyTo(disk, e + 28);
        return true;
    }

    /// <summary>
    /// A blank 1.44 MB data diskette as COS formats one: 512-byte sectors, a cluster a sector, two FATs of 9 sectors,
    /// 224 root entries; its boot sector only says it is no system diskette.
    /// </summary>
    public static byte[] Blank(string label = "NO NAME")
    {
        var disk = new byte[1474560];
        byte[] boot =
        {
            0xEB, 0x3C, 0x90, (byte)'C', (byte)'O', (byte)'S', (byte)'3', (byte)'.', (byte)'3', (byte)'0', (byte)' ',
            0x00, 0x02, 0x01, 0x01, 0x00, 0x02, 0xE0, 0x00, 0x40, 0x0B, 0xF0, 0x09, 0x00, 0x12, 0x00, 0x02, 0x00,
        };
        boot.CopyTo(disk, 0);
        disk[0x26] = 0x29;                      // extended boot record: serial, label, file system
        BitConverter.GetBytes(0x19860000u | (uint)label.Length).CopyTo(disk, 0x27);
        Encoding.ASCII.GetBytes(label.ToUpperInvariant().PadRight(11)[..11]).CopyTo(disk, 0x2B);
        Encoding.ASCII.GetBytes("FAT12   ").CopyTo(disk, 0x36);
        disk[0x3E] = 0xEB;                      // booted, it waits for ever
        disk[0x3F] = 0xFE;
        disk[510] = 0x55;
        disk[511] = 0xAA;
        for (int copy = 0; copy < 2; copy++)
        {
            int fat = 512 + copy * 9 * 512;
            disk[fat] = 0xF0;
            disk[fat + 1] = 0xFF;
            disk[fat + 2] = 0xFF;
        }
        return disk;
    }
}
