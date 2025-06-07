using BackupWarden.Models.Extensions;
using BackupWarden.Models;

namespace BackupWarden.Models
{
    public class PathIssue
    {
        public string PathSpec { get; }
        public string? ExpandedPath { get; }
        public PathIssueType IssueType { get; }
        public PathIssueSource Source { get; }
        public string Description { get; }

        public PathIssue(string pathSpec, string? expandedPath, PathIssueType issueType, PathIssueSource source, string description)
        {
            PathSpec = pathSpec;
            ExpandedPath = expandedPath;
            IssueType = issueType;
            Source = source;
            Description = description;
        }

        public override string ToString()
        {
            string mainPathToDisplay = ExpandedPath ?? PathSpec;
            string displayDescription = Description;

            if (!string.IsNullOrEmpty(mainPathToDisplay))
            {
                string quotedMainPath = $"'{mainPathToDisplay}'";
                if (displayDescription.Contains(quotedMainPath))
                {
                    displayDescription = displayDescription.Replace(quotedMainPath, "(this path)");
                }
            }
            return $"[{Source.ToDisplayString()} - {IssueType.ToDisplayString()}] {mainPathToDisplay}: {displayDescription}";
        }
    }
}