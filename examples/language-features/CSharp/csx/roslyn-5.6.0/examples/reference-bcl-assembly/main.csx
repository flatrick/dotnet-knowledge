#r "System.Xml.Linq"

using System.Xml.Linq;

System.Console.WriteLine(XDocument.Parse("<root />").Root!.Name.LocalName);
