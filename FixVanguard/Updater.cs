namespace FixVanguard;

// Antes hacia hot-swap del propio .exe. Ahora el update lo publica el equipo con
// FixVanguardUpdater y los usuarios lo descargan desde Discord. Solo dejamos la
// limpieza por si algún build viejo dejó restos.
internal static class Updater
{
    public static void CleanupOldBinary()
    {
        try
        {
            var current = Environment.ProcessPath;
            if (string.IsNullOrEmpty(current)) return;
            var backup = current + ".old";
            if (File.Exists(backup))
                File.Delete(backup);
        }
        catch
        {
            // Da igual; en el siguiente arranque se vuelve a intentar.
        }
    }
}
