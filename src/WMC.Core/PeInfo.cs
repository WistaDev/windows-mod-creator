using System.Reflection.PortableExecutable;

namespace WMC.Core;

public static class PeInfo
{
    /// <summary>
    /// true = roda como 64-bit; false = 32-bit; null = não é um PE reconhecido.
    /// Assemblies .NET AnyCPU (PE32 sem flag de 32 bits obrigatório) contam como 64-bit.
    /// </summary>
    public static bool? Is64Bit(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            var h = pe.PEHeaders;

            switch (h.CoffHeader.Machine)
            {
                case Machine.Amd64:
                case Machine.Arm64:
                    return true;
                case Machine.I386:
                    return h.CorHeader is not null && !h.CorHeader.Flags.HasFlag(CorFlags.Requires32Bit);
                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }
}
