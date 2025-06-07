using System.Linq;
using System.Text;
using BackupWarden.Models;
using BackupWarden.Models.Extensions; // For ModelEnumExtensions

namespace BackupWarden.Models.Extensions
{
    public static class AppSyncReportExtensions
    {
        public static string ToSummaryReport(this AppSyncReport report, int maxDistinctTypesToShow = 2)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Status: {report.OverallStatus.ToDisplayString()}");

            if (report.PathIssues.Count != 0)
            {
                var issuesBySource = report.PathIssues.GroupBy(i => i.Source);
                foreach (var group in issuesBySource.OrderBy(g => g.Key))
                {
                    sb.Append($"• {group.Key.ToDisplayString()} Path Issues: {group.Count()}");
                    var topPathIssues = group
                        .GroupBy(i => i.IssueType)
                        .Select(g => new { Type = g.Key, Count = g.Count() })
                        .OrderByDescending(x => x.Count)
                        .Take(maxDistinctTypesToShow)
                        .ToList();
                    if (topPathIssues.Count != 0)
                    {
                        sb.Append(" (");
                        sb.Append(string.Join(", ", topPathIssues.Select(tpi => $"{tpi.Count} {tpi.Type.ToDisplayString()}")));
                        if (group.GroupBy(i => i.IssueType).Count() > maxDistinctTypesToShow)
                        {
                            sb.Append(", ...");
                        }
                        sb.Append(')');
                    }
                    sb.AppendLine();
                }
            }

            if (report.FileDifferences.Count != 0)
            {
                sb.Append($"• File Differences: {report.FileDifferences.Count}");
                var topFileDiffs = report.FileDifferences
                    .GroupBy(d => d.DifferenceType)
                    .Select(g => new { Type = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(maxDistinctTypesToShow)
                    .ToList();
                if (topFileDiffs.Count != 0)
                {
                    sb.Append(" (");
                    sb.Append(string.Join(", ", topFileDiffs.Select(tfd => $"{tfd.Count} {tfd.Type.ToDisplayString()}")));
                    if (report.FileDifferences.GroupBy(d => d.DifferenceType).Count() > maxDistinctTypesToShow)
                    {
                        sb.Append(", ...");
                    }
                    sb.Append(')');
                }
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        public static string ToDetailedReport(this AppSyncReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Overall Status: {report.OverallStatus.ToDisplayString()}");
            if (report.PathIssues.Count != 0)
            {
                sb.AppendLine("Path Issues:");
                foreach (var issue in report.PathIssues.OrderBy(i => i.Source).ThenBy(i => i.IssueType))
                {
                    sb.AppendLine($"  - {issue}");
                }
            }
            if (report.FileDifferences.Count != 0)
            {
                sb.AppendLine("File Differences:");
                foreach (var diff in report.FileDifferences.OrderBy(d => d.DifferenceType).ThenBy(d => d.RelativePath))
                {
                    sb.AppendLine($"  - {diff}");
                }
            }

            if (report.OverallStatus == SyncStatus.NotYetBackedUp)
            {
                // For NotYetBackedUp, we expect the missing backup root issue and potentially "OnlyInApplication" file differences.
                // If these are the *only* things, the specific message is good. Otherwise, the listed issues/diffs provide detail.
                bool onlyExpectedElementsForNotYetBackedUp =
                    report.PathIssues.All(pi => (pi.Source == PathIssueSource.BackupLocation && pi.IssueType == PathIssueType.PathNotFound && pi.PathSpec == report.AppBackupRootPath) || pi.Source == PathIssueSource.Application) &&
                    report.PathIssues.Any(pi => pi.Source == PathIssueSource.BackupLocation && pi.IssueType == PathIssueType.PathNotFound && pi.PathSpec == report.AppBackupRootPath) &&
                    report.FileDifferences.All(fd => fd.DifferenceType == FileDifferenceType.OnlyInApplication);

                if (onlyExpectedElementsForNotYetBackedUp &&
                    // And if the only path issues are the missing root and/or non-critical app issues
                    report.PathIssues.Count(pi => !(pi.Source == PathIssueSource.BackupLocation && pi.IssueType == PathIssueType.PathNotFound && pi.PathSpec == report.AppBackupRootPath)) == report.PathIssues.Count(pi => pi.Source == PathIssueSource.Application))
                {
                    // If only the "NotYetBackedUp" condition is met (main backup folder missing)
                    // and any file differences are "OnlyInApplication", and any other path issues are non-critical app issues.
                    bool onlyMissingRootAndAppIssues = report.PathIssues.All(pi =>
                        (pi.Source == PathIssueSource.BackupLocation && pi.IssueType == PathIssueType.PathNotFound && pi.PathSpec == report.AppBackupRootPath) ||
                        (pi.Source == PathIssueSource.Application && (pi.IssueType == PathIssueType.PathIsEffectivelyEmpty || pi.IssueType == PathIssueType.PathNotFound))
                    );
                    if (onlyMissingRootAndAppIssues && report.FileDifferences.All(fd => fd.DifferenceType == FileDifferenceType.OnlyInApplication))
                    {
                        sb.AppendLine("Application data is present; no backup performed yet.");
                    }
                    // Otherwise, the listed issues/differences will provide the necessary detail.
                }
            }
            else if (report.PathIssues.Count == 0 && report.FileDifferences.Count == 0)
            {
                sb.AppendLine("No issues or differences found.");
            }
            return sb.ToString();
        }
    }
}