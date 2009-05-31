using System;
using System.Collections.Generic;
using System.Text;

namespace RTParser.Normalize
{
    /// <summary>
    /// Normalizes the input text into upper case
    /// </summary>
    public class MakeCaseInsensitive : RTParser.Utils.TextTransformer
    {
        public MakeCaseInsensitive(RTParser.RTPBot bot, Unifiable inputString) : base(bot, inputString)
        { }

        public MakeCaseInsensitive(RTParser.RTPBot bot) : base(bot) 
        { }

        protected override Unifiable ProcessChange()
        {
            return this.inputString.ToUpper();
        }

        /// <summary>
        /// An ease-of-use static method that re-produces the instance transformation methods
        /// </summary>
        /// <param name="input">The Unifiable to transform</param>
        /// <returns>The resulting Unifiable</returns>
        public static Unifiable TransformInput(Unifiable input)
        {
            return input.ToUpper();
        }
        public static Unifiable TransformInput(string input)
        {
            return Unifiable.Create(input.ToUpper());
        }
    }
}
