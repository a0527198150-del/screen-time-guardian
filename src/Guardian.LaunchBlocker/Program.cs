using System.Windows.Forms;

namespace ScreenTimeGuardian.LaunchBlocker;

/// <summary>
/// Launched by Windows in place of a blocked browser, via the IFEO Debugger value.
///
/// Its entire job is to tell the user what happened and exit. It deliberately does
/// nothing else: it starts no process, writes nothing, and never elevates. Windows
/// hands it the original command line as arguments, which we only use to name the
/// program that was blocked.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var programName = DescribeBlockedProgram(args);

        ApplicationConfiguration.Initialize();

        MessageBox.Show(
            $"ההפעלה של {programName} נחסמה על ידי שומר זמן מסך.\n\n" +
            "רק הדפדפנים המאושרים זמינים במחשב הזה.\n\n" +
            "אם זו חסימה שגויה, ניתן לשנות אותה בלוח הבקרה של האפליקציה, " +
            "או להשבית את החסימה עם הרשאת מנהל.",
            "שומר זמן מסך",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

        return 0;
    }

    private static string DescribeBlockedProgram(string[] args)
    {
        // IFEO passes the original command line. The first argument is the program path.
        var candidate = args.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "התוכנית";
        }

        try
        {
            var fileName = Path.GetFileName(candidate.Trim('"'));
            return string.IsNullOrWhiteSpace(fileName) ? "התוכנית" : fileName;
        }
        catch (ArgumentException)
        {
            return "התוכנית";
        }
    }
}
