Option Strict On

Namespace Vb14.XmlDocCommentImprovements
    ''' <summary>
    ''' VB 14 improved XML documentation comments: cref and param references
    ''' are validated and resolved by the compiler, and generic parameters,
    ''' operators, and partial members are handled correctly.
    ''' </summary>
    ''' <remarks>
    ''' A wrong <c>cref</c> is now reported rather than emitted silently, which
    ''' is what makes these comments trustworthy as documentation.
    ''' </remarks>
    Public Class Calculator(Of T)
        ''' <summary>Adds two values.</summary>
        ''' <param name="left">The left operand.</param>
        ''' <param name="right">The right operand.</param>
        ''' <returns>The sum.</returns>
        ''' <seealso cref="Subtract"/>
        Public Function Add(left As Integer, right As Integer) As Integer
            Return left + right
        End Function

        ''' <summary>Subtracts one value from another.</summary>
        ''' <param name="left">The left operand.</param>
        ''' <param name="right">The right operand.</param>
        ''' <returns>The difference.</returns>
        ''' <seealso cref="Add"/>
        Public Function Subtract(left As Integer, right As Integer) As Integer
            Return left - right
        End Function

        ''' <summary>
        ''' A reference to the enclosing type's own parameter, resolved by the
        ''' compiler: <typeparamref name="T"/>.
        ''' </summary>
        Public Function Identity(value As T) As T
            Return value
        End Function
    End Class
End Namespace
