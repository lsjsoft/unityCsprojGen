using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace UnityCsProjGen
{
    class Program
    {
        static string projectGuid = string.Empty;

        static string PickGuid(string inputStr)
        {
            Regex regex = new Regex("Project\\(\\\"\\{([^\\}]+)\\}");
            var Match = regex.Match(inputStr);
            if (Match.Success && Match.Groups.Count > 1)
            {
                return Match.Groups[1].Value;
            }

            return string.Empty;
        }

        static int Main(string[] args)
        {
            projectGuid = Guid.NewGuid().ToString();

            if (args.Length <= 1)
            {
                Console.WriteLine("Usage: UnityWorkDir, SourceFolderName, [TemplateFilename]");
                return -1;
            }

            var UnityWorkDir = System.IO.Path.GetFullPath(args[0]);
            var SourceFolderName= args[1];

            UnityWorkDir = UnityWorkDir.Replace("/", "\\");

            if (!UnityWorkDir.EndsWith("\\"))
            {
                UnityWorkDir += "\\";
            }

            var templateName = "Assembly-CSharp-Editor.csproj";
            if (args.Length > 2)
            {
                templateName = args[2];
            }

            var outputFilename = System.IO.Path.Combine(UnityWorkDir, SourceFolderName + ".csproj");
            var templateFilename = System.IO.Path.Combine(UnityWorkDir, templateName);

            if (!System.IO.File.Exists(templateFilename))
            {
                Console.WriteLine("FileNotExists:{0}", templateFilename);
                return -2;
            }

            if (!Execute(UnityWorkDir, SourceFolderName, templateFilename, outputFilename))
            {
                return -3;
            }

            var slnFname = System.IO.Path.GetFileNameWithoutExtension(UnityWorkDir.Substring(0, UnityWorkDir.Length-1)) + ".sln";
            var slnFilename = System.IO.Path.Combine(UnityWorkDir, slnFname);
            if (System.IO.File.Exists(slnFilename))
            {
                List<string> allLines = new List<string>();
                allLines.AddRange(System.IO.File.ReadAllLines(slnFilename));

                if ( FindIndexOf(allLines, SourceFolderName) < 0)
                {
                    var acecsprojh = FindIndexOf(allLines, "Assembly-CSharp-Editor.csproj");
                    if (acecsprojh > 0)
                    {
                        var ProjectConfig = allLines[acecsprojh];
                        var guid = PickGuid(ProjectConfig);
                        var insertProj = string.Format("Project(\"{{{0}}}\") = \"{1}\", \"{1}.csproj\", \"{{{2}}}\"",  guid, SourceFolderName, projectGuid);
                        allLines.Insert(acecsprojh + 2, insertProj);
                        allLines.Insert(acecsprojh + 3, "EndProject");
                    }

                    var gpIndex = FindIndexOf(allLines, "GlobalSection(ProjectConfigurationPlatforms)");
                    if (gpIndex > 0)
                    {
                        allLines.Insert(gpIndex + 1, string.Format("		{{{0}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU", projectGuid));
                        allLines.Insert(gpIndex + 2, string.Format("		{{{0}}}.Debug|Any CPU.Build.0 = Debug|Any CPU", projectGuid));
                    }

                    if (System.IO.File.Exists(slnFilename))
                    {
                        System.IO.File.Delete(slnFname);
                    }

                    System.IO.File.WriteAllLines(slnFilename, allLines);
                }
            }
            else
            {
                Console.WriteLine("WriteFail: " + slnFilename);
            }

            return 0;
        }

        static int FindIndexOf(List<string> input, string str)
        {
            for(int i=0; i<input.Count; ++i)
            {
                if ( input[i].IndexOf(str) > 0)
                {
                    return i;
                }
            }
            return -1;
        }

        static List<string> FormatCompileIncludes(string inputDir, string inputName)
        {
            List<string> output = new List<string>();
            System.Text.StringBuilder sb = new StringBuilder();
            var csFiles = System.IO.Directory.GetFiles(System.IO.Path.Combine(inputDir, inputName), "*.cs", System.IO.SearchOption.AllDirectories);
            foreach(var file in csFiles)
            {
                var fname = file.Replace(inputDir, @"");
                output.Add(fname);
            }
            return output;
        }
        

        static bool Execute(string inputDir, string inputName, string templateFilename, string outputFilename)
        {
            if (!System.IO.File.Exists(templateFilename))
            {
                Console.WriteLine( string.Format("Can't open {0}"), templateFilename);
                return false;
            }

            try
            {
                var xmlReader = new System.Xml.XmlTextReader(templateFilename);
                xmlReader.Namespaces = false;

                System.Xml.XmlDocument xmlDocument = new System.Xml.XmlDocument();
                xmlDocument.Load(xmlReader);

                // delete xmlns attribute first
                var xmlns = xmlDocument.DocumentElement.GetAttribute("xmlns");
                xmlDocument.DocumentElement.RemoveAttribute("xmlns");

                var filelist = FormatCompileIncludes(inputDir, inputName);

                if (!ReplaceItemGroup(xmlDocument, filelist))
                {
                    return false;
                }

                if (xmlns != null && xmlns.Length > 0)
                {
                    // append xmlns attribute
                    xmlDocument.DocumentElement.SetAttribute("xmlns", xmlns);
                }

                var ProjectGuid = xmlDocument.SelectSingleNode("Project/PropertyGroup/ProjectGuid");
                if (ProjectGuid != null)
                {
                    ProjectGuid.InnerText = "{" + projectGuid + "}";
                }

                if (System.IO.File.Exists(outputFilename))
                {
                    System.IO.File.Delete(outputFilename);
                }

                xmlDocument.Save(outputFilename);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Replace Files
        /// </summary>
        /// <param name="xmlDocument"></param>
        /// <param name="replaceList"></param>
        /// <returns></returns>
        private static bool ReplaceItemGroup(System.Xml.XmlDocument xmlDocument, List<string> replaceFiles)
        {
            var ItemGroups = xmlDocument.SelectNodes("/Project/ItemGroup");
            if (ItemGroups == null)
            {
                Console.WriteLine("Can't find Project/ItemGroup");
                return false;
            }

            foreach (System.Xml.XmlNode node in ItemGroups)
            {
                if (node.SelectSingleNode("ProjectReference") == null)
                {
                    var OldNodes = node.SelectNodes("Compile");
                    foreach (System.Xml.XmlNode child in OldNodes)
                    {
                        node.RemoveChild(child);
                    }

                    var firstChild = node.FirstChild;
                    foreach (var addNode in replaceFiles)
                    {
                        var Compile = xmlDocument.CreateElement("Compile");
                        Compile.SetAttribute("Include", addNode);

                        if (firstChild != null)
                        {
                            node.InsertBefore(Compile, firstChild);
                        }
                        else
                        {
                            node.AppendChild(Compile);
                        }
                    }

                    return true;
                }
            }

            Console.WriteLine("Can't find !ProjectReference node.");
            return false;
        }
    }
}
