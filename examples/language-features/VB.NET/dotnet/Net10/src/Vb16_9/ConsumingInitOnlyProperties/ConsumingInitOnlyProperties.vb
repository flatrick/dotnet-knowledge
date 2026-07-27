Option Strict On

Imports System.Text.Json.Schema

Namespace Vb16_9.ConsumingInitOnlyProperties
    Public Module InitOnly
        ' VB cannot DECLARE an init-only property — there is no VB equivalent of
        ' C#'s init accessor. VB 16.9 added the other half: it can SET one
        ' during object initialization, which earlier versions rejected outright.
        '
        ' JsonSchemaExporterOptions is a BCL type whose properties are
        ' init-only, so it serves as the C#-authored subject without this
        ' project needing a reference to one.
        Public Function SetDuringInitialization() As JsonSchemaExporterOptions
            Return New JsonSchemaExporterOptions With {
                .TreatNullObliviousAsNonNullable = True
            }
        End Function

        Public Function ReadBack() As Boolean
            Return SetDuringInitialization().TreatNullObliviousAsNonNullable
        End Function

        ' After construction the property is read-only, so an assignment here
        ' would not compile — the same guarantee a C# caller gets.
        Public Function ReadAfterConstruction(options As JsonSchemaExporterOptions) As Boolean
            Return options.TreatNullObliviousAsNonNullable
        End Function
    End Module
End Namespace
