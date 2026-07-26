Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Xml.Linq

Namespace Baseline.XmlLiterals
    Public Module Literals
        ' XML literals are VB-only: XML is part of the language grammar, and the
        ' compiler turns it into System.Xml.Linq calls. C# has no equivalent.
        Public Function Document() As XElement
            Return <catalog>
                       <item id="1">first</item>
                       <item id="2">second</item>
                   </catalog>
        End Function

        ' <%= %> embeds an expression, so the XML can be built from data.
        Public Function Embedded(name As String, count As Integer) As XElement
            Return <item name=<%= name %>><%= count %></item>
        End Function

        ' Axis properties query the tree with dedicated syntax: .<child> for
        ' child elements, .@name for an attribute, ...<descendant> for any depth.
        Public Function ChildValues() As List(Of String)
            Dim catalog = Document()
            Return catalog.<item>.Select(Function(item) item.Value).ToList()
        End Function

        Public Function FirstId() As String
            Return Document().<item>.First().@id
        End Function

        Public Function Descendants() As Integer
            Return Document()...<item>.Count()
        End Function

        ' A literal may be built from a loop with an embedded expression.
        Public Function Generated(values As IEnumerable(Of Integer)) As XElement
            Return <list><%= From value In values Select <value><%= value %></value> %></list>
        End Function
    End Module
End Namespace
