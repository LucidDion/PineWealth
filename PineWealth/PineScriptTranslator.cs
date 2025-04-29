using System.Security.Cryptography;
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
            string usings = usingClauses.ToStringNewLines();
            boilerPlate = boilerPlate.Replace("<#Using>", usings);
            string constructor = constructorBody.ToStringNewLines();
            boilerPlate = boilerPlate.Replace("<#Constructor>", constructor);

            //final cleanup
            boilerPlate = boilerPlate.Replace("WLColor . ", "WLColor.");

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
            string paneTag = "Price";
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
                    break;
                //same for indicator, except parse overlay
                else if (token == "indicator" && nextToken == "(")
                {
                    List<List<string>> indTokens = ExtractParameterTokens(tokens);
                    string overlay = GetKeyValue("overlay", indTokens);
                    if (overlay == "false")
                    {
                        string title = GetKeyValue("title", indTokens);
                        paneTag = title == null ? "NewPane" : title;
                    }
                    break;
                }
                //plot statement?
                else if (token == "plot")
                {
                    List<List<string>> plotTokens = ExtractParameterTokens(tokens);

                    //first parameter is series/value to plot - see if it's a numeric value
                    recurse++;
                    string arg1 = ConvertTokens(plotTokens[0]);

                    //pluck other arguments that we support
                    string argColor = GetKeyValue("color", plotTokens);
                    string argTitle = GetKeyValue("title", plotTokens);
                    string argLineWidth = GetKeyValue("linewidth", plotTokens);
                    string argStyle = GetKeyValue("style", plotTokens);
                    recurse--;

                    if (IsNumeric(arg1))
                    {
                        //DKK use DrawHorzLine for this one
                    }
                    else
                    {
                        //use PlotTimeSeries for series plots (this will handle indicators and TimeSeries)
                        if (argTitle == null)
                            argTitle = arg1;
                        if (argColor == null)
                            argColor = "Blue";
                        string plotLine = "PlotTimeSeries(" + arg1 + ", " + argTitle + ", \"" + paneTag + "\", " + argColor + ");";
                        AddToInitializeMethod(plotLine);
                    }

                    //line processing completed
                    break;
                }
                //color -> WLColor
                else if (token == "color" && nextToken == ".")
                {
                    outTokens.Add("WLColor");

                    //transform the color value token
                    int idxColor = i + 2;
                    if (idxColor < tokens.Count)
                    {
                        //DKK full color mapping
                        string colorVal = tokens[idxColor];
                        if (colorVal == "new")
                        {
                            //DKK new color
                        }
                        else if (colorVal == "rgb")
                        {
                            //DKK rgb color
                        }
                        else if (colorVal == "rgba")
                        {
                            //DKK rgba color
                        }
                        else
                            tokens[idxColor] = tokens[idxColor].ToProper();
                    }
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
                                    {
                                        string upperVar = tokens[1];
                                        InjectIndicator(upperVar, "BBUpper", 0, 1, 2);
                                        string baselineVar = tokens[3];
                                        InjectIndicator(baselineVar, "SMA", 0, 1);
                                        string lowerVar = tokens[5];
                                        InjectIndicator(lowerVar, "BBLower", 0, 1, 2);
                                    }
                                    break;
                                case "dc":
                                    {
                                        string upperDC = tokens[1];
                                        InjectIndicator(upperDC, "Highest", 0, 2);
                                        string lowerDC = tokens[3];
                                        InjectIndicator(lowerDC, "Lowest", 1, 2);
                                    }
                                    break;
                                case "ichimoku":
                                    {
                                        AddToUsing("WealthLab.IchimokuCloud");
                                        string tenkan = tokens[1];
                                        InjectIndicator(tenkan, "TenkanSen", "bars", 2);
                                        string kijun = tokens[3];
                                        InjectIndicator(kijun, "KijunSen", "bars", 3);
                                        string senkouA = tokens[5];
                                        InjectIndicator(senkouA, "SenkouSpanA", "bars", 2, 3, 5);
                                        string senkouB = tokens[5];
                                        InjectIndicator(senkouB, "SenkouSpanB", "bars", 2, 3, 5);
                                        string chikou = tokens[7];
                                        InjectIndicator(chikou, "ChikouSpan", "bars", 4);
                                    }
                                    break;
                                case "kc":
                                    {
                                        string upperKC = tokens[1];
                                        InjectIndicator(upperKC, "KeltnerUpper", "bars", 1, 2);
                                        string basisKC = tokens[3];
                                        InjectIndicator(basisKC, "EMA", 0, 1);
                                        string lowerKC = tokens[5];
                                        InjectIndicator(lowerKC, "KeltnerLower", "bars", 1, 2);
                                    }
                                    break;
                                case "macd":
                                    {
                                        string macdVar = tokens[1];
                                        InjectIndicator(macdVar, "MACD", 0, 1, 2);
                                        string signalVar = tokens[3];
                                        InjectIndicator(signalVar, "EMA", macdVar, 3);
                                        string histVar = tokens[5];
                                        InjectIndicator(histVar, "MACDHist", 0, 1, 2, 3);
                                    }
                                    break;
                                case "dmi":
                                    {
                                        string diplusVar = tokens[1];
                                        InjectIndicator(diplusVar, "DIPlus", "bars", 0);
                                        string diminusVar = tokens[3];
                                        InjectIndicator(diminusVar, "DIMinus", "bars", 0);
                                        string adxVar = tokens[5];
                                        InjectIndicator(adxVar, "ADX", "bars", 1);
                                    }
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
                    //input statement processing
                    if (nextToken == "input" && nextNextToken == ".")
                    {
                        if (tokens.Contains("(") && tokens.Contains(")"))
                        {
                            string paramName = prevToken;
                            i += 3;
                            if (i < tokens.Count)
                            {
                                switch (tokens[i])
                                {
                                    case "int":
                                        CreateParameter(paramName, ParameterType.Int32, tokens);
                                        break;
                                    case "float":
                                        CreateParameter(paramName, ParameterType.Double, tokens);
                                        break;
                                    case "bool":
                                        CreateParameter(paramName, ParameterType.Int32, tokens);
                                        break;
                                    case "string":
                                        throw new NotImplementedException();
                                    case "source":
                                        {
                                            //just interpret source inputs as closing price
                                            string decl = "private TimeSeries " + varName + ";";
                                            AddToVarDecl(decl);
                                            string init = varName + " = bars.Close;";
                                            AddToInitializeMethod(init);
                                        }
                                        break;
                                }
                            }
                        }
                        outTokens.Clear();
                        break; //this line is processed
                    }

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
                            //see if it's a mathematical operation, and if so does it involve series?
                            List<string> tokensAfter = GetTokensAfter("=", tokens);
                            bool isMath = false;
                            foreach (string tokenOp in tokensAfter)
                                if (mathOps.Contains(tokenOp))
                                {
                                    isMath = true;
                                    break;
                                }
                            if (isMath)
                            {
                                //determine if any of the terms are series
                                bool hasSeriesTerm = false;
                                for (int ta = 0; ta < tokensAfter.Count; ta++)
                                {
                                    string ta0 = tokensAfter[ta];
                                    string ta1 = ta + 1 < tokensAfter.Count ? tokensAfter[ta + 1] : null;
                                    if (ohclv.Contains(ta0))
                                        hasSeriesTerm = true;
                                    else if (ta0 == "ta" && ta1 == ".")
                                        hasSeriesTerm = true;
                                    if (hasSeriesTerm)
                                        break;
                                }

                                if (hasSeriesTerm)
                                {
                                    //TimeSeries
                                    varType = "TimeSeries";

                                    //process remainder of tokens and put in initialize
                                    string decl = "private " + varType + " " + varName + ";";
                                    AddToVarDecl(decl);
                                    recurse++;
                                    string statement = varName + " = " + ConvertTokens(tokensAfter) + " ;";
                                    recurse--;
                                    AddToInitializeMethod(statement);
                                    outTokens.Clear();
                                    break;
                                }
                                else
                                {
                                    //assume float
                                    varType = "double";
                                }
                            }
                            else
                            {
                                //assume float
                                varType = "double";
                            }
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
                    {
                        //special indicator handlers
                        bool handled = false;
                        List<string> tokensAfter = GetTokensAfter("=", tokens);
                        if (tokensAfter[0] == "ta" && tokensAfter[1] == ".")
                        {
                            recurse++;
                            indParams = ExtractParameterTokens(tokensAfter);
                            switch (tokensAfter[2])
                            {
                                case "alma":
                                    {
                                        handled = true;
                                        AddToUsing("WealthLab.AdvancedSmoothers");
                                        InjectIndicator(varName, "ALMA", 0, 1, 3, 2, "0");
                                    }
                                    break;
                                case "atr":
                                    {
                                        handled = true;
                                        InjectIndicator(varName, "ATR", "bars", 0);
                                    }
                                    break;
                                case "correlation":
                                    {
                                        handled = true;
                                        InjectIndicator(varName, "Corr", 0, 1, 2);
                                    }
                                    break;
                                case "crossover":
                                    {
                                        handled = true;
                                        DeclareVar(varName, "TimeSeries");
                                        string p1 = ConvertTokens(indParams[0]);
                                        string p2 = ConvertTokens(indParams[1]);
                                        string s = varName + " = " + p1.Trim() + ".CrossOver(" + p2 + ");";
                                        AddToInitializeMethod(s);
                                    }
                                    break;
                                case "crossunder":
                                    {
                                        handled = true;
                                        DeclareVar(varName, "TimeSeries");
                                        string p1 = ConvertTokens(indParams[0]);
                                        string p2 = ConvertTokens(indParams[1]);
                                        string s = varName + " = " + p1.Trim() + ".CrossUnder(" + p2 + ");";
                                        AddToInitializeMethod(s);
                                    }
                                    break;
                                case "cross":
                                    {
                                        handled = true;
                                        DeclareVar(varName, "TimeSeries");
                                        string p1 = ConvertTokens(indParams[0]);
                                        string p2 = ConvertTokens(indParams[1]);
                                        string s = varName + " = " + p1.Trim() + ".CrossOver(" + p2 + ") || " + p1.Trim() + ".CrossUnder(" + p2 + ");";
                                        AddToInitializeMethod(s);
                                    }
                                    break;
                                case "cum":
                                    {
                                        handled = true;
                                        DeclareVar(varName, "TimeSeries");
                                        string p1 = ConvertTokens(indParams[0]);
                                        string s = varName + " = " + p1.Trim() + ".Sum();";
                                        AddToInitializeMethod(s);
                                    }
                                    break;
                                case "falling":
                                    {
                                        handled = true;
                                        DeclareVar(varName, "TimeSeries");
                                        string p1 = ConvertTokens(indParams[0]);
                                        string p2 = ConvertTokens(indParams[1]);
                                        string s = varName + " = " + p1 + " < " + p1 + " >> " + p2 + ";";
                                        AddToInitializeMethod(s);
                                    }
                                    break;
                                case "rising":
                                    {
                                        handled = true;
                                        DeclareVar(varName, "TimeSeries");
                                        string p1 = ConvertTokens(indParams[0]);
                                        string p2 = ConvertTokens(indParams[1]);
                                        string s = varName + " = " + p1 + " > " + p1 + " >> " + p2 + ";";
                                        AddToInitializeMethod(s);
                                    }
                                    break;
                                case "highest":
                                    {
                                        handled = true;

                                        //can have one or two parameters
                                        if (indParams.Count == 1)
                                            InjectIndicator(varName, "Highest", "bars.High", 0);
                                        else
                                            InjectIndicator(varName, "Highest", 0, 1);
                                    }
                                    break;
                                case "lowest":
                                    {
                                        handled = true;

                                        //can have one or two parameters
                                        if (indParams.Count == 1)
                                            InjectIndicator(varName, "Lowest", "bars.Low", 0);
                                        else
                                            InjectIndicator(varName, "Lowest", 0, 1);
                                    }
                                    break;
                                case "highestbars":
                                    {
                                        handled = true;
                                        DeclareVar(varName, "TimeSeries");
                                        string p1 = ConvertTokens(indParams[0]);

                                        //can have one or two parameters
                                        if (indParams.Count == 1)
                                        {
                                            string s = varName + " = bars.High.HighestBars(" + p1 + ");";
                                            AddToInitializeMethod(s);
                                        }
                                        else
                                        {
                                            string p2 = ConvertTokens(indParams[1]);
                                            string s = varName + " = " + p1.Trim() + ".HighestBars(" + p2 + ");";
                                            AddToInitializeMethod(s);
                                        }
                                    }
                                    break;
                                case "lowestbars":
                                    {
                                        handled = true;
                                        DeclareVar(varName, "TimeSeries");
                                        string p1 = ConvertTokens(indParams[0]);

                                        //can have one or two parameters
                                        if (indParams.Count == 1)
                                        {
                                            string s = varName + " = bars.Low.LowestBars(" + p1 + ");";
                                            AddToInitializeMethod(s);
                                        }
                                        else
                                        {
                                            string p2 = ConvertTokens(indParams[1]);
                                            string s = varName + " = " + p1.Trim() + ".LowestBars(" + p2 + ");";
                                            AddToInitializeMethod(s);
                                        }
                                    }
                                    break;
                                case "hma":
                                    {
                                        handled = true;
                                        AddToUsing("WealthLab.AdvancedSmoothers");
                                        InjectIndicator(varName, "HMA", 0, 1);
                                    }
                                    break;
                            }
                            recurse--;
                        }
                        if (!handled)
                            throw new ArgumentException("Could not find matching WL indicator for " + token);
                        else
                        {
                            outTokens.Clear();
                            indicatorMapped = true;
                            break; //completes line processing
                        }
                    }
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
                else if (timeSeriesVars.Contains(token) && recurse == 0 && !indicatorMapped)
                {
                    outTokens.Add(token + "[idx]");
                }
                //strategy parameter?
                else if (_parameters.ContainsKey(token))
                {
                    string paramOut = token + ".";
                    Parameter p = _parameters[token];
                    if (p.Type == ParameterType.Int32)
                        paramOut += "AsInt";
                    else
                        paramOut += "AsDouble";
                    outTokens.Add(paramOut);
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
                if (indicatorMapped && execOut.Contains("="))
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

        //add to the using clause
        private void AddToUsing(string line)
        {
            string uc = "using " + line + ";";
            if (!usingClauses.Contains(uc))
                usingClauses.Add(uc);
        }

        //add to constructor
        private void AddToConstructor(string line)
        {
            line = "         " + line;
            constructorBody.Add(line);
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
            DeclareVar(varName, indName);

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

        //declare a variable
        private void DeclareVar(string varName, string varType)
        {
            if (!varDecl.Contains(varName))
            {
                string vd = "private " + varType + " " + varName + ";";
                AddToVarDecl(vd);
            }
        }

        //create a strategy parameter
        private void CreateParameter(string paramName, ParameterType pt, List<string> tokens)
        {
            //create parameter instance for tracking
            Parameter p = new Parameter();
            p.Type = pt;
            p.Name = paramName;
            _parameters[paramName] = p;

            //create declaration statement
            string dec = "private Parameter " + paramName + ";";
            AddToVarDecl(dec);

            //parse out parameters of the Pine Script input statement - first parameter after ( is the default value
            int idx = tokens.IndexOf("(");
            if (idx == -1)
                return;
            idx++;
            if (idx >= tokens.Count)
                return;
            string defaultVal = tokens[idx];
            if (defaultVal == "true")
                defaultVal = "1";
            if (defaultVal == "false") 
                defaultVal = "0";

            //DKK parse start/stop/step

            //get title
            string paramTitle = paramName;
            idx = tokens.IndexOf("title");
            if (idx > 0)
            {
                idx += 2;
                if (idx < tokens.Count)
                    paramTitle = tokens[idx];
            }

            //creating constructor statement
            string cons = paramName + " = AddParameter(" + paramTitle + ", ParameterType." + pt + ", " + defaultVal + ");";
            AddToConstructor(cons);
        }

        //given a list of List<tokens>, return the value portion of the specified key
        private string GetKeyValue(string key, List<List<string>> tokenLists)
        {
            foreach(List<string> list in tokenLists)
            {
                if (list.Count > 1 && list[0] == key && list[1] == "=")
                {
                    List<string> valueList = new List<string>();
                    for (int n = 2; n < list.Count; n++)
                        valueList.Add(list[n]);
                    return ConvertTokens(valueList);
                }
            }
            return null;
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
        private List<string> usingClauses = new List<string>();
        private List<string> constructorBody = new List<string>();
        private static List<string> ohclv = new List<string>() { "open", "high", "low", "close", "volume" };
        private static List<string> mathOps = new List<string>() { "+", "-", "*", "/" };
        List<List<string>> indParams;
        private static Dictionary<string, string> pvIndicators = new Dictionary<string, string>() { { "ema", "EMA" }, { "rsi", "RSI" }, { "sma", "SMA" }, { "barssince", "BarsSince" },
            { "bbw", "BBWidth" }, { "cci", "CCI" }, { "cmo", "CMO" }, { "cog", "CG" }, { "correlation", "Corr" }, { "dev", "MeanAbsDev" } };
        private Dictionary<string, Parameter> _parameters = new Dictionary<string, Parameter>();
    }
}