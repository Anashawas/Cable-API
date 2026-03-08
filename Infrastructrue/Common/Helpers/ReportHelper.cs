namespace Infrastructrue.Common.Helpers;

public static class ReportHelper
{
    public static string SetUpReportPath(string basePath, string reportName)
        => Path.Combine(basePath, reportName + ".frx");
}