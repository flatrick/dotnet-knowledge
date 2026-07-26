Option Strict On

Namespace Baseline.TypeMembers
    Public Class Account
        ' A constant is evaluated at compile time.
        Public Const MaxRetries As Integer = 3

        ' Instance and Shared variables.
        Private _balance As Decimal
        Private Shared _accountCount As Integer

        ' A parameterized constructor, and a Shared one that runs once per type.
        Public Sub New(balance As Decimal)
            _balance = balance
            _accountCount += 1
        End Sub

        Shared Sub New()
            _accountCount = 0
        End Sub

        ' A property with both accessors written out.
        Public Property Balance As Decimal
            Get
                Return _balance
            End Get
            Set(value As Decimal)
                _balance = value
            End Set
        End Property

        Public Shared ReadOnly Property Count As Integer
            Get
                Return _accountCount
            End Get
        End Property

        ' A Sub returns nothing; a Function returns a value.
        Public Sub Deposit(amount As Decimal)
            _balance += amount
        End Sub

        Public Function Withdraw(amount As Decimal) As Boolean
            If amount > _balance Then
                Return False
            End If

            _balance -= amount
            Return True
        End Function

        ' An event, declared and raised.
        Public Event Changed(sender As Object, amount As Decimal)

        Public Sub Apply(amount As Decimal)
            _balance += amount
            RaiseEvent Changed(Me, amount)
        End Sub

        ' An operator overload.
        Public Shared Operator +(left As Account, right As Account) As Account
            Return New Account(left._balance + right._balance)
        End Operator
    End Class
End Namespace
