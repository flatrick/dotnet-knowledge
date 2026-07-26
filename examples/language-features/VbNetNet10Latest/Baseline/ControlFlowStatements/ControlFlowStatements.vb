Option Strict On

Namespace Baseline.ControlFlowStatements
    Public Module ControlFlow
        Public Function Classify(value As Integer) As String
            If value < 0 Then
                Return "negative"
            ElseIf value = 0 Then
                Return "zero"
            Else
                Return "positive"
            End If
        End Function

        ' Select Case supports single values, comma-separated lists, ranges with
        ' To, and comparisons with Is — more shapes than a C# switch label.
        Public Function Band(value As Integer) As String
            Select Case value
                Case 0
                    Return "zero"
                Case 1, 2, 3
                    Return "small"
                Case 4 To 9
                    Return "medium"
                Case Is >= 10
                    Return "large"
                Case Else
                    Return "negative"
            End Select
        End Function

        Public Function SumTo(limit As Integer) As Integer
            Dim total As Integer = 0

            ' For with an explicit Step.
            For i As Integer = 1 To limit Step 1
                total += i
            Next

            Return total
        End Function

        Public Function CountItems(values As Integer()) As Integer
            Dim count As Integer = 0

            For Each value As Integer In values
                count += 1
            Next

            Return count
        End Function

        Public Function CountDown(start As Integer) As Integer
            Dim steps As Integer = 0
            Dim current As Integer = start

            ' Do While tests before the body; Do Until tests for the negation.
            Do While current > 0
                current -= 1
                steps += 1
            Loop

            Return steps
        End Function

        Public Function AtLeastOnce() As Integer
            Dim runs As Integer = 0

            ' The Loop While form always executes the body at least once.
            Do
                runs += 1
            Loop While False

            Return runs
        End Function

        Public Function FirstNegative(values As Integer()) As Integer
            For Each value As Integer In values
                If value < 0 Then
                    ' Exit leaves the nearest enclosing loop.
                    Return value
                End If
            Next

            Return 0
        End Function

        Public Function SkipZeros(values As Integer()) As Integer
            Dim total As Integer = 0

            For Each value As Integer In values
                If value = 0 Then
                    ' Continue skips to the next iteration.
                    Continue For
                End If

                total += value
            Next

            Return total
        End Function
    End Module
End Namespace
