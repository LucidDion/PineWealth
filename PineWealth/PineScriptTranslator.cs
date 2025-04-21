using System.Runtime.CompilerServices;
using System.Text;
using WealthLab.Core;

namespace WealthLab.Backtest
{
    //Translates Pine Script into a WL8 C# Strategy
    //DKK combine N . N, N . and . N patterns into single tokens before processing
    public class PineScriptTranslator
    {
        //perform the translation
        public string Translate(string pineScriptSource, string boilerPlate)
        {
            //remove returns
            pineScriptSource = pineScriptSource.Replace("\r", "");

            //split source into lines
            List<string> lines = pineScriptSource.Split('\n').ToList();

            //split lines separated by semicolon
            List<string> originalLines = new List<string>(lines);
            lines.Clear();
            foreach (string line in originalLines)
                lines.AddRange(SplitStatements(line));

            //process lines
            for (int n = 0; n < lines.Count; n++)
            {
                string line = lines[n];

                //comment?
                if (line.TrimStart().StartsWith("//"))
                    continue;

                //remove embedded comment
                int idx = line.IndexOf("//");
                if (idx >= 0)
                    line = line.Substring(0, idx);

                //convert every 4 leading spaces into an indentation token to track code blocks
                string indentString = "";
                string replaceString = "";
                int indentCount = 0;
                bool isIndented = false;
                do
                {
                    indentString += "    ";
                    if (line.StartsWith(indentString))
                    {
                        isIndented = true;
                        replaceString += "\t";
                        isIndented = true;
                        indentCount++;
                    }
                    else
                        isIndented = false;
                }
                while (isIndented);
                if (indentCount > 0)
                    line = line.Replace(indentString, replaceString);

                //ignore blanks
                line = line.Trim();
                if (line == "")
                    continue;

                //see if the current indent level has decreased, if so we need to add a closing brace
                while(indentCount < indentLevel)
                {
                    indentLevel--;
                    AddToExecuteMethod("}");
                }

                //get tokens
                List<string> tokens = TokenizeLine(line);
                tokens = CombineFloatTokens(tokens);

                //process
                ifAdded = false;
                string execOut = ConvertTokens(tokens);
                if (execOut != null && !indicatorMapped)
                {
                    //did we add an if or other indenting statement?
                    AddToExecuteMethod(execOut);

                    if (ifAdded)
                    {
                        AddToExecuteMethod("{");
                        indentLevel++;
                    }
                }
            }

            //final closing braces
            while(indentLevel > 0)
            {
                indentLevel--;
                AddToExecuteMethod("}");
            }

            //replace boilerplate tags with generated code
            string vd = varDecl.ToStringNewLines();
            boilerPlate = boilerPlate.Replace("<#VarDecl>", vd);
            string init = initializeBody.ToStringNewLines();
            boilerPlate = boilerPlate.Replace("<#Initialize>", init);
            string exec = executeBody.ToStringNewLines();
            boilerPlate = boilerPlate.Replace("<#Execute>", exec);

            return boilerPlate;
        }

        //given a list of tokens, returns the converted C# string
        private string ConvertTokens(List<string> tokens)
        {
            //our output tokens
            List<string> outTokens = new List<string>();

            //process tokens
            bool mapIndicator = false;
            bool needsSemicolon = true;
            string varName = "";
            string varType = "var";
            indicatorMapped = false;

            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                string prevToken = i == 0 ? null : tokens[i - 1];
                string nextToken = i + 1 < tokens.Count ? tokens[i + 1] : null;
                string nextNextToken = i + 2 < tokens.Count ? tokens[i + 2] : null;

                //tab?
                if (token == "\t")
                    continue;
                //whitespace?
                else if (token.Trim() == "")
                    break;
                //first token = strategy? if so, ignore that line
                else if (token == "strategy" && nextToken == "(")
                {
                    break;
                }
                //tuple assignment (square bracket as first token)?
                else if (token == "[" && i == 0)
                {
                    //special logic to determine which indicator produced the result, create an intermediate indicator result and afterward split them out
                    int closeBracketIdx = tokens.IndexOf("]");
                    if (closeBracketIdx > i)
                    {
                        List<string> tokensAfter = GetTokensAfter("=", tokens);
                        if (tokensAfter[0] == "ta" && tokensAfter[1] == ".")
                        {
                            recurse++;
                            indParams = ExtractParameterTokens(tokensAfter);
                            switch (tokensAfter[2])
                            {
                                case "bb":
                                    string upperVar = tokens[1];
                                    InjectIndicator(upperVar, "BBUpper", 0, 1, 2);
                                    string baselineVar = tokens[3];
                                    InjectIndicator(baselineVar, "SMA", 0, 1);
                                    string lowerVar = tokens[5];
                                    InjectIndicator(lowerVar, "BBLower", 0, 1, 2);
                                    break;
                                case "macd":
                                    string macdVar = tokens[1];
                                    InjectIndicator(macdVar, "MACD", 0, 1, 2);
                                    string signalVar = tokens[3];
                                    InjectIndicator(signalVar, "EMA", macdVar, 3);
                                    string histVar = tokens[5];
                                    InjectIndicator(histVar, "MACDHist", 0, 1, 2, 3);
                                    break;
                            }
                            recurse--;
                            break; //this completes processing of this line
                        }
                    }
                    else
                        throw new ArgumentException("Closing tuple bracket not found.");
                }
                //assignment?
                else if (token == "=")
                {
                    //ensure variable is declared
                    if (prevToken != null)
                        varName = prevToken;
                    outTokens.Add("=");

                    //deduce variable type
                    if (i + 1 < tokens.Count)
                    {
                        string varVal = tokens[i + 1];
                        if (varVal == "true" || varVal == "false")
                            varType = "bool";
                        else if (varVal.StartsWith("\""))
                            varType = "string";
                        else if (IsNumeric(varVal))
                        {
                            //numeric value - default to double
                            varType = "double";
                        }
                        else
                        {
                            //assume float
                            varType = "double";
                        }
                    }
                }
                //ta. indicator declaration?
                else if (token == "ta" && nextToken == ".")
                {
                    i++;
                    mapIndicator = true;
                }
                //map this token as an indicator
                else if (mapIndicator)
                {
                    if (!timeSeriesVars.Contains(varName))
                        timeSeriesVars.Add(varName);
                    mapIndicator = false;
                    indicatorMapped = true;
                    if (pvIndicators.ContainsKey(token))
                    {
                        string wlInd = pvIndicators[token];
                        outTokens.Add(wlInd + ".Series");
                        varType = wlInd;
                    }
                    else
                        throw new ArgumentException("Could not find matching WL indicator for " + token);
                }
                else if (token == "(")
                {
                    parensCount++;
                    outTokens.Add("(");
                }
                else if (token == ")")
                {
                    parensCount--;
                    outTokens.Add(")");
                }
                else if (token == "and")
                {
                    outTokens.Add("&&");
                }
                else if (token == "or")
                {
                    outTokens.Add("||");
                }
                else if (token == "not")
                {
                    outTokens.Add("!");
                }
                //OHLCV param values
                else if (ohclv.Contains(token) && (parensCount > 0 || recurse > 0))
                {
                    string suffix = indicatorMapped || recurse > 0 ? "" : "[idx]";
                    outTokens.Add("bars." + token.ToProper() + suffix);
                }
                //number?
                else if (IsNumeric(token))
                {
                    //is it floating point?
                    if (nextToken == "." && IsNumeric(nextNextToken))
                    {
                        outTokens.Add(token + "." + nextNextToken);
                        i += 2;
                    }
                    else
                        outTokens.Add(token);
                }
                //if statement
                else if (token == "if")
                {
                    needsSemicolon = false;
                    outTokens.Add(token);
                    ifAdded = true;
                }
                //strategy entry
                else if (token == "strategy" && nextToken == "." && nextNextToken == "entry")
                {
                    string sigName = tokens[i + 4];
                    sigName = sigName.Replace("\"", "");
                    bool isLong = tokens[i + 8] == "long";
                    string tt = isLong ? "Buy" : "Short";
                    string pt = "PlaceTrade(bars, TransactionType." + tt + ", OrderType.Market, 0, \"" + sigName + "\");";
                    outTokens.Add(pt);

                    //add the code to close opposing order
                    if (isLong)
                    {
                        AddToExecuteMethod("if (HasOpenPosition(bars, PositionType.Short))");
                        AddToExecuteMethod("   PlaceTrade(bars, TransactionType.Cover, OrderType.Market);");
                    }
                    else
                    {
                        AddToExecuteMethod("if (HasOpenPosition(bars, PositionType.Long))");
                        AddToExecuteMethod("   PlaceTrade(bars, TransactionType.Sell, OrderType.Market);");
                    }

                    break; //stop token processing on this line
                }
                //needs time series index?
                else if (timeSeriesVars.Contains(token))
                {
                    outTokens.Add(token + "[idx]");
                }
                //output it as is
                else
                {
                    outTokens.Add(token);
                }
            }

            //if we have a line, process it
            if (outTokens.Count > 0)
            {
                //add semicolon
                if (needsSemicolon && recurse == 0)
                    if (outTokens.Count == 0 || !outTokens[outTokens.Count - 1].EndsWith(";"))
                        outTokens.Add(";");

                //build and add output line
                string execOut = "";
                foreach (string ot in outTokens)
                    execOut += ot + " ";
                if (indicatorMapped)
                    AddToInitializeMethod(execOut.Trim());

                //did we declare a variable?
                if (varName != "")
                    AddToVarDecl("private " + varType + " " + varName + ";");

                //return output
                return execOut;
            }
            else
                return null;
        }

        //combine floating point tokens
        private List<string> CombineFloatTokens(List<string> tokens)
        {
            List<string> combined = new List<string>();
            for (int n = 0; n < tokens.Count; n++)
            {
                bool wasCombined = false;
                string token = tokens[n];
                string token1 = n + 1 < tokens.Count ? tokens[n + 1] : null;
                string token2 = n + 2 < tokens.Count ? tokens[n + 2] : null;
                int n1 = n + 1;
                int n2 = n + 2;
                if (n2 < tokens.Count && IsNumeric(token) && token1 == "." && IsNumeric(token2))
                {
                    wasCombined = true;
                    combined.Add(token + "." + token2);
                    n += 2;
                }
                if (!wasCombined && IsNumeric(token) && token1 == ".")
                {
                    wasCombined = true;
                    combined.Add(token + ".0");
                    n++;
                }
                if (!wasCombined && token == "." && IsNumeric(token1))
                {
                    wasCombined = true;
                    combined.Add("0." + token1);
                    n++;
                }
                if (!wasCombined)
                    combined.Add(token);
            }
            return combined;
        }

        //split a line into tokens
        private List<string> TokenizeLine(string line)
        {
            var tokens = new List<string>();
            var token = new StringBuilder();
            var separators = new HashSet<char> { '(', ')', '[', ']', '{', '}', ',', '.', '+', '-', '*', '/', '%', '=', '<', '>', '!', ':', ';', '\t' };
            int i = 0;

            while (i < line.Length)
            {
                char c = line[i];

                // Handle string literals
                if (c == '"')
                {
                    AddToken(token, tokens); // flush before string
                    token.Append(c);
                    i++;
                    while (i < line.Length)
                    {
                        token.Append(line[i]);
                        if (line[i] == '"' && line[i - 1] != '\\') break;
                        i++;
                    }
                    tokens.Add(token.ToString());
                    token.Clear();
                    i++;
                }
                // Handle comments (// or #)
                else if ((c == '/' && i + 1 < line.Length && line[i + 1] == '/') || c == '#')
                {
                    AddToken(token, tokens);
                    break; // Ignore rest of the line
                }
                // Whitespace
                else if (char.IsWhiteSpace(c))
                {
                    AddToken(token, tokens);
                    i++;
                }
                // Punctuation / operators
                else if (separators.Contains(c))
                {
                    AddToken(token, tokens);

                    // Handle two-char operators
                    if (i + 1 < line.Length)
                    {
                        string twoCharOp = $"{c}{line[i + 1]}";
                        if (twoCharOp is "==" or "!=" or ">=" or "<=" or "&&" or "||" or "++" or "--")
                        {
                            tokens.Add(twoCharOp);
                            i += 2;
                            continue;
                        }
                    }

                    tokens.Add(c.ToString());
                    i++;
                }
                else
                {
                    token.Append(c);
                    i++;
                }
            }

            AddToken(token, tokens);
            return tokens;
        }
        private void AddToken(StringBuilder token, List<string> tokens)
        {
            if (token.Length > 0)
            {
                tokens.Add(token.ToString());
                token.Clear();
            }
        }

        //aplit lines separated by semicolon
        public List<string> SplitStatements(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            int parenDepth = 0, bracketDepth = 0, braceDepth = 0;

            foreach (char c in line)
            {
                switch (c)
                {
                    case '(':
                        parenDepth++;
                        break;
                    case ')':
                        parenDepth--;
                        break;
                    case '[':
                        bracketDepth++;
                        break;
                    case ']':
                        bracketDepth--;
                        break;
                    case '{':
                        braceDepth++;
                        break;
                    case '}':
                        braceDepth--;
                        break;
                    case ';':
                        if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                        {
                            result.Add(current.ToString());
                            current.Clear();
                            continue;
                        }
                        break;
                }

                current.Append(c);
            }

            if (current.Length > 0)
                result.Add(current.ToString());

            return result;
        }

        //is a token numeric?
        private bool IsNumeric(string token)
        {
            if (token == null)
                return false;
            int n;
            return Int32.TryParse(token, out n);
        }

        //add a line to var decl
        private void AddToVarDecl(string line)
        {
            varDecl.Add("      " + line.Trim());
        }

        //add a line to initialize
        private void AddToInitializeMethod(string line)
        {
            line = "         " + line.Trim();
            initializeBody.Add(line);
        }

        //add a line to execute method
        private void AddToExecuteMethod(string line)
        {
            line = "         " + line;
            for (int i = 0; i < indentLevel; i++)
                line = "   " + line;
            executeBody.Add(line);
        }

        //given a list of tokens, extract lists that represent a list of parameters, ie (P1, P2, P3) would return three lists of tokens
        private List<List<string>> ExtractParameterTokens(List<string> tokens)
        {
            List<string> arguments = new List<string>();
            List<List<string>> result = new List<List<string>>();

            //remove tokens "(" and preceding
            int idx = tokens.IndexOf("(");
            if (idx == -1)
                return result;
            arguments.AddRange(tokens.Skip(idx + 1));

            //remove final ")"
            if (arguments.Count == 0)
                return result;
            if (arguments[arguments.Count - 1] == ")")
                arguments.RemoveAt(arguments.Count - 1);

            //start processing tokens
            int parenCount = 0;
            List<string> currentArg = new List<string>();
            for(int n = 0; n < arguments.Count; n++)
            {
                if (parenCount == 0 && arguments[n] == ",")
                {
                    result.Add(currentArg);
                    currentArg = new List<string>();
                }
                else
                {
                    currentArg.Add(arguments[n]);
                    if (arguments[n] == "(")
                        parenCount++;
                    else if (arguments[n] == ")")
                        parenCount--;
                }
            }

            //add final argument
            if (currentArg.Count > 0)
                result.Add(currentArg);

            return result;
        }

        //get the tokens beyond the specified token
        private List<string> GetTokensAfter(string token, List<string> tokens)
        {
            int idx = tokens.IndexOf(token);
            if (idx == -1)
                return new List<string>();
            return tokens.Skip(idx + 1).ToList();
        }

        //inject an indicator - that was the result of a tuple assignment in PineScript
        private void InjectIndicator(string varName, string indName, params object[] arguments)
        {
            //ignore underscore outputs
            if (varName == "_")
                return;

            //ensure the variable is defined
            if (!varDecl.Contains(varName))
            {
                string vd = "private " + indName + " " + varName + ";";
                AddToVarDecl(vd);
            }

            //create it in initialize
            string indCreate = varName + " = " + indName + ".Series(";
            for(int n = 0; n  < arguments.Length; n++)
            {
                object obj = arguments[n];
                if (obj is string)
                    indCreate += (string)obj;
                else
                {
                    int pIdx = (int)arguments[n];
                    string paramText = ConvertTokens(indParams[pIdx]);
                    indCreate += paramText;
                }
                if (n != arguments.Length - 1)
                    indCreate = indCreate.TrimEnd() + ", ";
            }
            indCreate += ");";
            AddToInitializeMethod(indCreate);
        }

        //variables
        private bool indicatorMapped = false;
        private bool ifAdded = false;
        private int recurse = 0;
        private int parensCount = 0;
        private int indentLevel = 0;
        private List<string> initializeBody = new List<string>();
        private List<string> executeBody = new List<string>();
        private List<string> varDecl = new List<string>();
        private List<string> timeSeriesVars = new List<string>();
        private static List<string> ohclv = new List<string>() { "open", "high", "low", "close", "volume" };
        List<List<string>> indParams;
        private static Dictionary<string, string> pvIndicators = new Dictionary<string, string>() { { "ema", "EMA" }, { "rsi", "RSI" }, { "sma", "SMA" } };
    }
}