using System;
using System.Collections.Generic;
using System.Drawing;

namespace MissionPlanner.Warnings
{
    public class CustomWarning
    {
        public static object defaultsrc { get; set; }

        System.Reflection.PropertyInfo Item { get; set; }

        System.Reflection.PropertyInfo Item2 { get; set; }

        System.Reflection.PropertyInfo Item3 { get; set; }

        public CustomWarning Child = null;

        public enum Conditional
        {
            NONE = 0,
            LT,
            LTEQ,
            EQ,
            GT,
            GTEQ,
            NEQ
        }

        public enum MathOp
        {
            NONE = 0,
            ADD,
            SUB,
            MUL,
            DIV,
            MOD
        }

        public enum WarningColors
        {
            NoColor = 0,
            Red,
            OrangeRed,
            Maroon,
            Yellow,
            Gold,
            Goldenrod,
            LawnGreen,
            Green,
            DarkGreen
        }

        // Differentiate between speak/text and QV Coloring items
        public enum WarningType
        {
            SpeakAndText = 0,
            Coloring
        }

        public CustomWarning()
        {
            RepeatTime = 10;
            Warning = 0;
            ConditionType = Conditional.NONE;
            Operator1 = MathOp.NONE;
            Operator2 = MathOp.NONE;
        }

        /// <summary>
        /// Return the list of options that are avaliable
        /// </summary>
        public List<string> GetOptions()
        {
            if (defaultsrc == null)
                throw new ArgumentNullException("src");

            List<string> answer = new List<string>();

            Type test = defaultsrc.GetType();

            foreach (var field in test.GetProperties())
            {
                // field.Name has the field's name.
                object fieldValue;
                TypeCode typeCode;
                try
                {
                    fieldValue = field.GetValue(defaultsrc, null); // Get value

                    if (fieldValue == null)
                        continue;

                    // Get the TypeCode enumeration. Multiple types get mapped to a common typecode.
                    typeCode = Type.GetTypeCode(fieldValue.GetType());
                }
                catch
                {
                    continue;
                }

                answer.Add(field.Name);
            }

            return answer;
        }

        public string Name
        {
            get { return _name; }
            set
            {
                if (_name == value)
                {
                    return;
                }
                _name = value;
                if (defaultsrc != null) SetField(value);
            }
        }

        string _name = "";

        public string Name2
        {
            get { return _name2; }
            set
            {
                if (_name2 == value)
                {
                    return;
                }
                _name2 = value ?? "";
                if (defaultsrc != null) SetField2(_name2);
            }
        }

        string _name2 = "";

        public string Name3
        {
            get { return _name3; }
            set
            {
                if (_name3 == value)
                {
                    return;
                }
                _name3 = value ?? "";
                if (defaultsrc != null) SetField3(_name3);
            }
        }

        string _name3 = "";

        public bool HasSecondField
        {
            get { return Item2 != null && !string.IsNullOrEmpty(_name2); }
        }

        public bool HasThirdField
        {
            get { return Item3 != null && !string.IsNullOrEmpty(_name3); }
        }

        /// <summary>
        /// Operator applied between Field 1 and Field 2.
        /// </summary>
        public MathOp Operator1 { get; set; }

        /// <summary>
        /// Operator applied between (Field1 op1 Field2) and Field 3.
        /// </summary>
        public MathOp Operator2 { get; set; }

        /// <summary>
        /// True when an arithmetic expression is being built on the left-hand side
        /// </summary>
        public bool HasExpression
        {
            get
            {
                return (Operator1 != MathOp.NONE && HasSecondField) ||
                       (Operator2 != MathOp.NONE && HasThirdField);
            }
        }

        /// <summary>
        /// Warning on this number based on ConditionType
        /// </summary>
        public double Warning { get; set; }

        /// <summary>
        /// used to track last time something was said
        /// </summary>
        DateTime lastrepeat;

        /// <summary>
        /// identify the type of warning  (SpeakAndText or Coloring)
        /// </summary>
        public WarningType type { get; set; }

        /// <summary>
        /// this color is used when warning fired
        /// </summary>
        public string color { get; set; }

        /// <summary>
        /// in seconds
        /// </summary>
        public int RepeatTime { get; set; }

        /// <summary>
        /// how we are checking Warning
        /// </summary>
        public Conditional ConditionType { get; set; }

        /// <summary>
        /// What we are going to say. use {warning}, {value}, {name}
        /// </summary>
        public string Text
        {
            get { return _text; }
            set { _text = value; }
        }

        string _text = "WARNING: {name} is {value}";

        /// <summary>
        /// Returns the formated string to pass to the speech engine
        /// </summary>
        /// <returns></returns>
        public string SayText()
        {
            return
                Text.Replace("{warning}", Warning.ToString("0.##"))
                    .Replace("{lhs}", EvaluateLeft().ToString("0.##"))
                    .Replace("{value3}", HasThirdField ? GetValue3.ToString("0.##") : "")
                    .Replace("{name3}", HasThirdField ? Item3.Name : "")
                    .Replace("{value2}", HasSecondField ? GetValue2.ToString("0.##") : Warning.ToString("0.##"))
                    .Replace("{name2}", HasSecondField ? Item2.Name : Warning.ToString("0.##"))
                    .Replace("{value}", GetValue.ToString("0.##"))
                    .Replace("{name}", Item.Name);
        }

        /// <summary>
        /// Get the current value
        /// </summary>
        public double GetValue
        {
            get
            {
                if (defaultsrc == null)
                    throw new ArgumentNullException("src");
                if (Item == null)
                    throw new ArgumentNullException("Item");

                return (double) Convert.ChangeType(Item.GetValue(defaultsrc, null), typeof (double));
            }
        }

        public double GetValue2
        {
            get
            {
                if (defaultsrc == null)
                    throw new ArgumentNullException("src");
                if (Item2 == null)
                    throw new ArgumentNullException("Item2");

                return (double)Convert.ChangeType(Item2.GetValue(defaultsrc, null), typeof(double));
            }
        }

        public double GetValue3
        {
            get
            {
                if (defaultsrc == null)
                    throw new ArgumentNullException("src");
                if (Item3 == null)
                    throw new ArgumentNullException("Item3");

                return (double)Convert.ChangeType(Item3.GetValue(defaultsrc, null), typeof(double));
            }
        }

        private static double Apply(double left, MathOp op, double right)
        {
            switch (op)
            {
                case MathOp.ADD: return left + right;
                case MathOp.SUB: return left - right;
                case MathOp.MUL: return left * right;
                case MathOp.DIV: return right == 0 ? double.NaN : left / right;
                case MathOp.MOD: return right == 0 ? double.NaN : left % right;
                default: return left;
            }
        }

        /// <summary>
        /// Evaluates the left-hand side expression:
        /// Field1 [Op1 Field2] [Op2 Field3]
        /// Evaluated strictly left-to-right (no operator precedence).
        /// </summary>
        public double EvaluateLeft()
        {
            double v = GetValue;

            if (Operator1 != MathOp.NONE && HasSecondField)
                v = Apply(v, Operator1, GetValue2);

            if (Operator2 != MathOp.NONE && HasThirdField)
                v = Apply(v, Operator2, GetValue3);

            return v;
        }

        /// <summary>
        /// return true on match, and uses repeat time to prevent spamming
        /// </summary>
        /// <returns></returns>
        public bool CheckValue(bool userepeattime = true)
        {
            if (userepeattime && DateTime.Now < lastrepeat.AddSeconds(RepeatTime))
                return false;

            bool condition = false;

            double lhs = EvaluateLeft();

            double rhs = (!HasExpression && HasSecondField) ? GetValue2 : Warning;

            if (double.IsNaN(lhs) || double.IsNaN(rhs))
                return false;

            switch (ConditionType)
            {
                case Conditional.EQ:
                    if (lhs == rhs)
                        condition = true;
                    break;
                case Conditional.GT:
                    if (lhs > rhs)
                        condition = true;
                    break;
                case Conditional.GTEQ:
                    if (lhs >= rhs)
                        condition = true;
                    break;
                case Conditional.LT:
                    if (lhs < rhs)
                        condition = true;
                    break;
                case Conditional.LTEQ:
                    if (lhs <= rhs)
                        condition = true;
                    break;
                case Conditional.NEQ:
                    if (lhs != rhs)
                        condition = true;
                    break;
                case Conditional.NONE:

                    break;
            }

            if (condition && userepeattime)
            {
                lastrepeat = DateTime.Now;
            }

            return condition;
        }


        public void SetField(string name)
        {
            if (defaultsrc == null)
                throw new ArgumentNullException("src");

            if (name == "")
                return;

            Type test = defaultsrc.GetType();

            foreach (var field in test.GetProperties())
            {
                if (field.Name == name)
                {
                    Item = field;
                    Name = name;
                    return;
                }
            }

            throw new MissingFieldException("No such name");
        }

        public void SetField2(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Item2 = null;
                _name2 = "";
                return;
            }

            if (defaultsrc == null)
                throw new ArgumentNullException("src");

            Type test = defaultsrc.GetType();

            foreach (var field in test.GetProperties())
            {
                if (field.Name == name)
                {
                    Item2 = field;
                    _name2 = name;
                    return;
                }
            }

            throw new MissingFieldException("No such name");
        }

        public void SetField3(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Item3 = null;
                _name3 = "";
                return;
            }

            if (defaultsrc == null)
                throw new ArgumentNullException("src");

            Type test = defaultsrc.GetType();

            foreach (var field in test.GetProperties())
            {
                if (field.Name == name)
                {
                    Item3 = field;
                    _name3 = name;
                    return;
                }
            }

            throw new MissingFieldException("No such name");
        }
    }
}