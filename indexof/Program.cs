using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace indexof
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(IndexOfOpeningCharacter());
        }

        public static int IndexOfOpeningCharacter()
        {
            //byte[] a = new byte[16] { 0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15}; // input a
            //byte[] b = new byte[16] { 0x80, 1,2,3,4,5,6,77,8,9,10,11,12,13,14,15 }; // input b
            //byte[] r = new byte[16]; // output r
            string text = "The cat in the hat";
            int start = 0;
            int end = text.Length - 1;
            string needle = "cat";

            Debug.Assert(text is not null);
            Debug.Assert(start >= 0 && end >= 0);
            Debug.Assert(end - start + 1 >= 0);
            Debug.Assert(end - start + 1 <= text.Length);

            // assume all ascii

            long bitmap_0_3 = 0;
            long bitmap_4_7 = 0;

            foreach (char openingChar in needle)
            {
                int position = (openingChar >> 4) | ((openingChar & 0x0F) << 3);
                if (position < 64) bitmap_0_3 |= 1L << position;
                else bitmap_4_7 |= 1L << (position - 64);
            }

            Vector128<byte> bitmap = Vector128.Create(bitmap_0_3, bitmap_4_7).AsByte();

            if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian)
            {
                // Based on http://0x80.pl/articles/simd-byte-lookup.html#universal-algorithm
                // Optimized for sets in the [1, 127] range

                int lengthMinusOne = end - start;
                int charsToProcessVectorized = lengthMinusOne & ~(2 * Vector128<short>.Count - 1);
                int finalStart = start + charsToProcessVectorized;

                if (start < finalStart)
                {
                    ref char textStartRef = ref Unsafe.Add(ref Unsafe.AsRef(in text.GetPinnableReference()), start);
                    do
                    {
                        // Load 32 bytes (16 chars) into two Vector128<short>s (chars)
                        // Drop the high byte of each char
                        // Pack the remaining bytes into a single Vector128<byte>
                        Vector128<byte> input = Sse2.PackUnsignedSaturate(
                            Unsafe.ReadUnaligned<Vector128<short>>(ref Unsafe.As<char, byte>(ref textStartRef)),
                            Unsafe.ReadUnaligned<Vector128<short>>(ref Unsafe.As<char, byte>(ref Unsafe.Add(ref textStartRef, Vector128<short>.Count))));

                        // Extract the higher nibble of each character ((input >> 4) & 0xF)
                        Vector128<byte> higherNibbles = Sse2.And(Sse2.ShiftRightLogical(input.AsUInt16(), 4).AsByte(), Vector128.Create((byte)0xF));

                        // Lookup the matching higher nibble for each character based on the lower nibble
                        // PSHUFB will set the result to 0 for any non-ASCII (> 127) character
                        Vector128<byte> bitsets = Ssse3.Shuffle(bitmap, input);

                        // Calculate a bitmask (1 << (higherNibble % 8)) for each character
                        Vector128<byte> bitmask = Ssse3.Shuffle(Vector128.Create(0x8040201008040201).AsByte(), higherNibbles);

                        // Check which characters are present in the set
                        // We are relying on bitsets being zero for non-ASCII characters
                        Vector128<byte> result = Sse2.And(bitsets, bitmask);

                        if (!result.Equals(Vector128<byte>.Zero))
                        {
                            int resultMask = ~Sse2.MoveMask(Sse2.CompareEqual(result, Vector128<byte>.Zero));
                            return start + BitOperations.TrailingZeroCount((uint)resultMask);
                        }

                        start += 2 * Vector128<short>.Count;
                        textStartRef = ref Unsafe.Add(ref textStartRef, 2 * Vector128<short>.Count);
                    }
                    while (start != finalStart);
                }
            }

            throw new NotImplementedException();

            //Console.WriteLine(utilities.ToReadableByteArray(a));
            //Console.WriteLine(utilities.ToReadableByteArray(b));
            //Console.WriteLine(utilities.ToReadableByteArray(r));
        }
    }
}