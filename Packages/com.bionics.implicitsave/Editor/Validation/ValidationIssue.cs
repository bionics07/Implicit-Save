using System;

namespace ImplicitSave.Editor
{
    /// <summary>How much a validation finding matters.</summary>
    public enum ValidationSeverity
    {
        /// <summary>Works, but is probably not what was meant.</summary>
        Info,

        /// <summary>Data will be lost or will not behave as the author expects.</summary>
        Warning,

        /// <summary>The save type cannot work as written.</summary>
        Error
    }

    /// <summary>
    /// One problem found in a save type. Every issue names the member and says what to do about it -
    /// a validator that only says "invalid" costs more support than it saves.
    /// </summary>
    public readonly struct ValidationIssue
    {
        /// <summary>The save type the issue was found in.</summary>
        public readonly Type SaveType;

        /// <summary>The field or member at fault, or <c>null</c> when the whole type is.</summary>
        public readonly string MemberName;

        /// <summary>How much it matters.</summary>
        public readonly ValidationSeverity Severity;

        /// <summary>What is wrong and how to fix it.</summary>
        public readonly string Message;

        /// <param name="saveType">The save type the issue was found in.</param>
        /// <param name="memberName">The member at fault, or <c>null</c> for the type itself.</param>
        /// <param name="severity">How much it matters.</param>
        /// <param name="message">What is wrong and how to fix it.</param>
        public ValidationIssue(Type saveType, string memberName, ValidationSeverity severity, string message)
        {
            SaveType = saveType;
            MemberName = memberName;
            Severity = severity;
            Message = message;
        }

        /// <summary>A single line ready for the console.</summary>
        public override string ToString()
        {
            var where = string.IsNullOrEmpty(MemberName)
                ? SaveType?.Name
                : $"{SaveType?.Name}.{MemberName}";

            return $"{where}: {Message}";
        }
    }
}
