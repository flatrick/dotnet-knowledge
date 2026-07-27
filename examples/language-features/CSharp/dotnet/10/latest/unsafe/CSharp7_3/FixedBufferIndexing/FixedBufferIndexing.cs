namespace Net10_CSharpLatest_Unsafe.CSharp7_3.FixedBufferIndexing
{
    public unsafe struct Packet
    {
        public fixed byte Payload[4];
    }

    public unsafe class Holder
    {
        public Packet Packet;
    }

    public unsafe class Reader
    {
        // Holder.Packet is a fixed buffer reached through a class field, so it is
        // movable: the object holding it can be relocated by the GC. Before C#
        // 7.3 that meant it had to be pinned before it could be indexed (error
        // CS8320 without pinning). C# 7.3 lets the compiler index it directly
        // through a managed pointer instead, so no pinning is required.
        public static byte FirstByte(Holder holder)
        {
            return holder.Packet.Payload[0];
        }

        public static int Total(Holder holder)
        {
            int total = 0;
            for (int i = 0; i < 4; i++)
            {
                total += holder.Packet.Payload[i];
            }

            return total;
        }

        // Contrast: a fixed buffer reached through a parameter of the struct
        // type itself is already a fixed variable - its storage cannot move -
        // so indexing it directly has always been legal, even before C# 7.3.
        public static byte FirstByteFromParameter(Packet packet)
        {
            return packet.Payload[0];
        }
    }
}
