using System;
using System.Collections.Generic;
using System.Linq;

namespace CopilotTaskbarApp.Controls
{
    internal class FileTypesHelpers
    {
        public static List<string> ConvertMediaTypeToFileType(List<string> mediaTypes)
        {
            var fileTypes = new List<string>();
            foreach (var mediaType in mediaTypes)
            {
                switch (mediaType.ToLower())
                {
                    case "image/jpeg":
                        fileTypes.Add(".jpg");
                        fileTypes.Add(".jpeg");
                        break;
                    case "image/png":
                        fileTypes.Add(".png");
                        break;
                    case "image/gif":
                        fileTypes.Add(".gif");
                        break;
                    case "image/webp":
                        fileTypes.Add(".webp");
                        break;
                    case "application/pdf":
                        fileTypes.Add(".pdf");
                        break;
                    default:
                        // Do nothing if unknown  
                        break;
                }
            }
            return fileTypes;
        }

        public static IEnumerable<string> GetAllTextExtensions()
        {
            return
            [
                ".txt", ".md", ".csv", ".json", ".xml", ".html", ".htm", 
                ".log", ".yaml", ".yml", ".ini", ".cfg", ".bat", ".sh", 
                ".py", ".js", ".ts", ".c", ".cpp", ".h", ".hpp", ".cs", 
                ".java", ".go", ".rs", ".swift", ".kt", ".m", ".php", ".rb", 
                ".pl", ".sql", ".r", ".tex", ".toml", ".properties", ".conf", 
                ".env", ".scss", ".css", ".sass", ".less", ".vue", ".jsx", 
                ".tsx", ".dart", ".scala", ".erl", ".ex", ".exs", ".lua", 
                ".groovy", ".ps1", ".psm1", ".vb", ".vbs", ".fsharp", ".fs", 
                ".fsx", ".asm", ".s", ".dockerfile", ".makefile", ".mk", 
                ".cmake", ".gradle", ".gemspec", ".rake", ".gql", ".graphql", ".ipynb"
            ];
        }

        public static IEnumerable<string> GetAllImageExtensions()
        {
            return new[]
            {
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", 
                ".tif", ".ico", ".svg", ".heic", ".avif"
            };
        }

        public static IEnumerable<string> GetAllSupportedExtensions()
        {
            return GetAllTextExtensions().Concat(GetAllImageExtensions()).Concat(new[] { ".pdf" }).Distinct();
        }

        public static bool IsSupportedFileExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return false;
            return GetAllSupportedExtensions().Contains(extension.ToLowerInvariant());
        }
    }
}
