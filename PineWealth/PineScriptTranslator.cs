using System.Net.Http.Headers;
using System.Text;
using WealthLab.Core;

namespace WealthLab.Backtest
{
    //Translates Pine Script into a WL8 C# Strategy
    //DKK combine N . N, N . and . N patterns into single tokens before processing
    public class PineScriptTranslator
    {
        //current line mode
        public LineMode LineMode { get; set; }

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
                ifStatement = false;

                //obtain line
                string line = lines[n];
                LineMode = LineMode.Scalar;

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

                //version 3 does not use ta. library, prepend ta.
                foreach(string taInd in taIndicators)
                {
                    string oldStyle = " " + taInd + "(";
                    string newStyle = " ta." + taInd + "(";
                    line = line.Replace(oldStyle, newStyle);
                    oldStyle = "(" + taInd + "(";
                    newStyle = "( ta." + taInd + "(";
                    line = line.Replace(oldStyle, newStyle);
                }

                //handle default input type
                line = line.Replace("input(", "input.int(");

                //see if the current indent level has decreased, if so we need to add a closing brace
                while(indentCount < indentLevel)
                {
                    indentLevel--;
                    AddToExecuteMethod("}");
                }

                //get tokens
                List<string> tokens = TokenizeLine(line);
                tokens = CombineFloatTokens(tokens);
                tokens = CombineLibTokens(tokens);
                string token = tokens[0];
                string nextToken = tokens.Count < 2 ? null : tokens[1];
                string nextNextToken = tokens.Count < 3 ? null : tokens[2];

                //process
                if (nextToken == "=")
                {
                    //assignment statement
                    string varName = token;
                    string varType = "double";
                    LineMode = LineMode.Scalar;
                    tokens.RemoveAt(0);
                    tokens.RemoveAt(0);

                    //deduce variable type
                    if (tokens.Count > 0)
                    {
                        string varVal = tokens[0];
                        if (varVal == "input")
                        {
                            varType = "Parameter";
                            ParameterType pt = ParameterType.Double;
                            switch(tokens[2])
                            {
                                case "int":
                                    pt = ParameterType.Int32;
                                    break;
                                case "source":
                                    pt = ParameterType.PriceComponent;
                                    break;
                            }
                            if (tokens[2] == "int")
                                pt = ParameterType.Int32;
                            CreateParameter(varName, pt, tokens);
                            continue;
                        }
                        else if (varVal == "color")
                            varType = "WLColor";
                        else if (varVal == "true" || varVal == "false")
                            varType = "bool";
                        else if (varVal.StartsWith("\""))
                            varType = "string";
                        else if (tokens.Contains("ta."))
                        {
                            varType = "TimeSeries";
                            LineMode = LineMode.Series;
                        }
                        else if (tokens.Intersect(ohclv).Any())
                        {
                            varType = "TimeSeries";
                            LineMode = LineMode.Series;
                        }
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
                                    if (ohclv.Contains(ta0))
                                        hasSeriesTerm = true;
                                    else if (ta0 == "ta.")
                                        hasSeriesTerm = true;
                                    if (hasSeriesTerm)
                                        break;
                                }

                                if (hasSeriesTerm)
                                {
                                    //TimeSeries
                                    varType = "TimeSeries";
                                    LineMode = LineMode.Series;

                                    //process remainder of tokens and put in initialize
                                    DeclareVar(varName, varType);
                                    recurse++;
                                    string statement = varName + " = " + ConvertTokens(tokensAfter) + " ;";
                                    recurse--;
                                    AddToInitializeMethod(statement);
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

                    //remember time series types
                    if (varType == "TimeSeries")
                        if (!timeSeriesVars.Contains(varName))
                            timeSeriesVars.Add(varName);

                    //construct the assignment statement
                    string assignmentLine = varName + " = ";
                    assignmentLine += ConvertTokens(tokens) + ";";
                    DeclareVar(varName, varType);
                    if (LineMode == LineMode.Scalar)
                        AddToExecuteMethod(assignmentLine);
                    else
                        AddToInitializeMethod(assignmentLine);
                }
                else if (line.Trim().StartsWith("//"))
                {
                    //full line comment
                    prevComments.Add(line);
                }
                else if (token == "strategy" && nextToken == "(")
                {
                    //NOP
                }
                else if (token == "indicator" && nextToken == "(")
                {
                    //assign pane tag from indicator token
                    List<List<string>> indTokens = ExtractParameterTokens(tokens);
                    string overlay = GetKeyValue("overlay", indTokens);
                    if (overlay == "false")
                    {
                        string title = GetKeyValue("title", indTokens);
                        paneTag = title == null ? "NewPane" : title;
                    }
                    continue;
                }
                else if (token == "plot")
                {
                    LineMode = LineMode.Series;

                    //plot statement
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
                }
                else if (token == "bgcolor")
                {
                    LineMode = LineMode.Series;
                    List<List<string>> bgcTokens = ExtractParameterTokens(tokens);
                    string bgColor = GetKeyValue("color", bgcTokens);
                    string bgTrans = GetKeyValue("transp", bgcTokens);
                    foreach (List<string> args in bgcTokens)
                    {
                        if (bgTrans == null && args.Count == 1 && IsNumeric(args[0]))
                            bgTrans = args[0];
                        if (bgColor == null)
                        {
                            bgColor = ConvertTokens(args);
                            break;
                        }
                    }
                    if (bgColor != null)
                    {
                        if (bgTrans != null)
                            bgColor += ".MakeTransparent((byte)(" + bgTrans + "  * 2.55))";
                        string bgLine = "SetBackgroundColor(bars, idx, " + bgColor + ");";
                        AddToExecuteMethod(bgLine);
                    }
                }
                else if (token == "strategy" && nextToken == "." && nextNextToken == "entry")
                {
                    //strategy entry
                    string sigName = tokens[4];
                    sigName = sigName.Replace("\"", "");
                    bool isLong = tokens[8] == "long";
                    string tt = isLong ? "Buy" : "Short";
                    string pt = "PlaceTrade(bars, TransactionType." + tt + ", OrderType.Market, 0, \"" + sigName + "\");";

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

                    //add entry trade
                    AddToExecuteMethod(pt);
                }
                else if (token == "if")
                {
                    //if statement
                    ifStatement = true;
                    LineMode = LineMode.Scalar;

                    //parens?
                    if (nextToken != "(")
                    {
                        tokens.Insert(1, "(");
                        tokens.Add(")");
                    }

                    //compose line
                    string ifLine = ConvertTokens(tokens);
                    AddToExecuteMethod(ifLine);
                    AddToExecuteMethod("{");
                    indentLevel++;
                }
                else if (token == "[")
                {
                    //tuple assignment
                    //special logic to determine which indicator produced the result, create an intermediate indicator result and afterward split them out
                    LineMode = LineMode.Series;
                    int closeBracketIdx = tokens.IndexOf("]");
                    if (closeBracketIdx > 0)
                    {
                        List<string> tokensAfter = GetTokensAfter("=", tokens);
                        if (tokensAfter[0] == "ta.")
                        {
                            recurse++;
                            indParams = ExtractParameterTokens(tokensAfter);
                            switch (tokensAfter[1])
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
                        }
                    }
                    else
                        throw new ArgumentException("Closing tuple bracket not found.");
                }
                else
                {
                    //fallback - vanilla processing
                    string outLine = ConvertTokens(tokens);
                    if (outLine != null)
                    {
                        if (!outLine.Trim().EndsWith("\t"))
                            outLine += ";";
                        AddToExecuteMethod(outLine);
                    }
                }
            }

            //final closing braces
            while(indentLevel > 0)
            {
                indentLevel--;
                AddToExecuteMethod("}");
            }

            //add variable declarations
            foreach(KeyValuePair<string, string> kvp in varTypes)
            {
                string line = "private " + kvp.Value + " " + kvp.Key + ";";
                AddToVarDecl(line);
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
            boilerPlate = boilerPlate.Replace("else ( if", "else if (");
            boilerPlate = boilerPlate.Replace(" .Make", ".Make");

            return boilerPlate;
        }

        //given a list of tokens, returns the converted C# string
        private string ConvertTokens(List<string> tokens)
        {
            //our output tokens
            List<string> outTokens = new List<string>();

            //process tokens
            string varName = "";
            string varType = "var";
            indicatorMapped = false;

            //inject implied assignments from if/else-assignment
            if (ifAssignment != null)
            {
                tokens.Insert(0, "=");
                tokens.Insert(0, ifAssignment);
                ifAssignment = null;
            }
            if (elseAssignment != null)
            {
                tokens.Insert(0, "=");
                tokens.Insert(0, elseAssignment);
                elseAssignment = null;
            }

            //process each token
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
                            //new color -> MakeTransparent
                            List<List<string>> colorNewParams = ExtractParameterTokens(tokens);
                            recurse++;
                            string colorNewArg1 = ConvertTokens(colorNewParams[0]);
                            colorNewArg1 = colorNewArg1.Replace("WLColor ", "");
                            string colorNewArg2 = ConvertTokens(colorNewParams[1]);
                            recurse--;
                            outTokens.Add(colorNewArg1.Trim() + ".MakeTransparent((byte)(" + colorNewArg2 + " * 2.55))");
                            RemoveTokensUpTo(tokens, ")", i, true);
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
                //ta. indicator declaration?
                else if (token == "ta.")
                {
                    LineMode ls = LineMode;
                    LineMode = LineMode.Series;

                    //map a ta lib indicator to a WL indicator, and advance the token counter to the next token that needs to be processed
                    i++;
                    string indName = tokens[i];
                    i++;
                    if (tokens[i] != "(")
                        throw new InvalidOperationException("Expected opening parenthesis.");
                    i++;
                    List<string> argTokens = ExtractArgumentTokens(tokens, i);
                    i--;

                    //standard mapping
                    string timeSeriesString = null;
                    if (pvIndicators.ContainsKey(indName))
                    {
                        string wlInd = pvIndicators[indName];
                        string args = ConvertTokens(argTokens);
                        timeSeriesString = wlInd + ".Series(" + args + ")";
                    }
                    else
                    {
                        //DKK customized mappings
                        indParams = ExtractParameterTokens(argTokens, true);
                        switch(indName)
                        {
                            case "alma":
                                AddToUsing("WealthLab.AdvancedSmoothers");
                                timeSeriesString = GenerateInlineIndicator("ALMA", 0, 1, 3, 2, "0");
                                break;
                            case "atr":
                                timeSeriesString = GenerateInlineIndicator("ATR", "bars", 0);
                                break;
                            case "crossover":
                                {
                                    string source1 = ConvertTokens(indParams[0]).Trim();
                                    string source2 = ConvertTokens(indParams[1]).Trim();
                                    timeSeriesString = source1 + ".CrossOver(" + source2 + ")";
                                }
                                break;
                            case "crossunder":
                                {
                                    string source1 = ConvertTokens(indParams[0]).Trim();
                                    string source2 = ConvertTokens(indParams[1]).Trim();
                                    timeSeriesString = source1 + ".CrossUnder(" + source2 + ")";
                                }
                                break;
                            case "cross":
                                {
                                    string source1 = ConvertTokens(indParams[0]).Trim();
                                    string source2 = ConvertTokens(indParams[1]).Trim();
                                    timeSeriesString = source1 + ".CrossOver(" + source2 + ") | " + source1 + ".CrossUnder(" + source2 + ")";
                                }
                                break;
                            case "cum":
                                {
                                    string source = ConvertTokens(indParams[0]).Trim();
                                    timeSeriesString = source + ".Sum()";
                                }
                                break;
                            case "falling":
                                {
                                    string source = ConvertTokens(indParams[0]).Trim();
                                    string lookback = ConvertTokens(indParams[1]);
                                    timeSeriesString = source + " < " + "(" + source + " >> " + lookback + ")";
                                }
                                break;
                            case "rising":
                                {
                                    string source = ConvertTokens(indParams[0]).Trim();
                                    string lookback = ConvertTokens(indParams[1]);
                                    timeSeriesString = source + " > " + "(" + source + " >> " + lookback + ")";
                                }
                                break;
                            case "highest":
                                {
                                    string source = indParams.Count == 1 ? "bars.High" : ConvertTokens(indParams[0]);
                                    string period = indParams.Count == 1 ? ConvertTokens(indParams[0]) : ConvertTokens(indParams[1]);
                                    timeSeriesString = "Highest.Series(" + source + "," + period + ")";
                                }
                                break;
                            case "lowest":
                                {
                                    string source = indParams.Count == 1 ? "bars.Low" : ConvertTokens(indParams[0]);
                                    string period = indParams.Count == 1 ? ConvertTokens(indParams[0]) : ConvertTokens(indParams[1]);
                                    timeSeriesString = "Lowest.Series(" + source + "," + period + ")";
                                }
                                break;
                            case "highestbars":
                                {
                                    string source = indParams.Count == 1 ? "bars.High" : ConvertTokens(indParams[0]).Trim();
                                    string period = indParams.Count == 1 ? ConvertTokens(indParams[0]) : ConvertTokens(indParams[1]);
                                    timeSeriesString = source + ".HighestBars(" + period + ")";
                                }
                                break;
                            case "lowestbars":
                                {
                                    string source = indParams.Count == 1 ? "bars.Low" : ConvertTokens(indParams[0]).Trim();
                                    string period = indParams.Count == 1 ? ConvertTokens(indParams[0]) : ConvertTokens(indParams[1]);
                                    timeSeriesString = source + ".LowestBars(" + period + ")";
                                }
                                break;
                            case "hma":
                                AddToUsing("WealthLab.AdvancedSmoothers");
                                timeSeriesString = GenerateInlineIndicator("HMA", 0, 1);
                                break;
                        }
                    }

                    //restore LineMode
                    LineMode = ls;

                    //inject generated time indictor definition
                    if (ifStatement)
                    {
                        //define a new TimeSeries variable for this indicator, and use variable name here instead of indicator definition
                        varName = "ts" + dynamicVarCount;
                        varType = "TimeSeries";
                        dynamicVarCount++;
                        DeclareVar(varName, "TimeSeries");
                        string tsInit = varName + " = " + timeSeriesString + ";";
                        AddToInitializeMethod(tsInit);
                        outTokens.Add(varName + "[idx]");
                    }
                    else
                        outTokens.Add(timeSeriesString);
                }
                else if (token == "(")
                {
                    outTokens.Add("(");
                }
                else if (token == ")")
                {
                    outTokens.Add(")");
                }
                else if (token == "and")
                {
                    if (LineMode == LineMode.Series)
                        outTokens.Add("&");
                    else
                        outTokens.Add("&&");
                }
                else if (token == "or")
                {
                    if (LineMode == LineMode.Series)
                        outTokens.Add("|");
                    else
                        outTokens.Add("||");
                }
                else if (token == "not")
                {
                    outTokens.Add("!");
                }
                //OHLCV param values
                else if (ohclv.Contains(token))
                {
                    string outToken = token.ToProper();
                    if (LineMode == LineMode.Scalar)
                        outToken += "[idx]";
                    outToken = "bars." + outToken;
                    outTokens.Add(outToken);
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
                else if (timeSeriesVars.Contains(token) && recurse == 0 && !indicatorMapped && LineMode != LineMode.Series)
                {
                    outTokens.Add(token + "[idx]");
                }
                else if (timeSeriesVars.Contains(token) && LineMode == LineMode.Scalar)
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
                //build and add output line
                string execOut = "";
                foreach (string ot in outTokens)
                    execOut += ot + " ";
                if (indicatorMapped && execOut.Contains("="))
                    AddToInitializeMethod(execOut.Trim());

                //did we declare a variable?
                if (varName != "")
                    DeclareVar(varName, varType);

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

        //combine ta. into one token
        private List<string> CombineLibTokens(List<string> tokens)
        {
            List<string> outTokens = new List<string>();
            for(int n = 0; n < tokens.Count; n++)
            {
                if (tokens[n] == "ta" && n < tokens.Count - 1 && tokens[n + 1] == ".")
                {
                    outTokens.Add("ta.");
                    n++;
                }
                else
                    outTokens.Add(tokens[n]);
            }
            return outTokens;
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
            line = "      " + line.Trim();
            if (!varDecl.Contains(line))
                varDecl.Add(line);
        }

        //add a line to initialize
        private void AddToInitializeMethod(string line)
        {
            line = line.Replace("[idx]", "");
            initializeBody.AddRange(prevComments);
            prevComments.Clear();
            line = "         " + line.Trim();
            initializeBody.Add(line);
        }

        //add a line to execute method
        private void AddToExecuteMethod(string line)
        {
            executeBody.AddRange(prevComments);
            prevComments.Clear();

            //pre-process indentation
            line = "         " + line;
            for (int i = 0; i < indentLevel; i++)
                line = "   " + line;

            //post process the line - handle cases where we may be comparing boolean series 
            line = line.Replace("else ( if", "else if (");
            if (line.Contains("if (") && line.Contains("["))
            {
                bool hasOp = false;
                foreach (string logOp in logicalOps)
                    if (line.Contains(logOp))
                    {
                        hasOp = true;
                        break;
                    }
                if (!hasOp)
                    line = line.Replace(" )", " > 0 )");
            }

            //add the line
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
        private List<List<string>> ExtractParameterTokens(List<string> tokens, bool avoidSkip = false)
        {
            List<string> arguments = new List<string>();
            List<List<string>> result = new List<List<string>>();

            //remove tokens "(" and preceding
            int idx = tokens.IndexOf("(");
            if (idx >= 0 && tokens[tokens.Count - 1] == ")" && !avoidSkip)
                arguments.AddRange(tokens.Skip(idx + 1));
            else
                arguments.AddRange(tokens);

            //remove final ")"
            if (arguments.Count == 0)
                return result;
            if (arguments[arguments.Count - 1] == ")" && !avoidSkip)
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

        //generate an inline indicator declaration
        private string GenerateInlineIndicator(string indName, params object[] arguments)
        {
            string indOut = indName + ".Series(";
            for(int n = 0; n < arguments.Length; n++)
            {
                object obj = arguments[n];
                if (obj is string)
                    indOut += (string)obj;
                else
                {
                    int pIdx = (int)obj;
                    string paramText = ConvertTokens(indParams[pIdx]);
                    indOut += paramText;
                }
                if (n != arguments.Length - 1)
                    indOut += ", ";
            }
            indOut += ")";
            return indOut;
        }

        //declare a variable
        private void DeclareVar(string varName, string varType)
        {
            varTypes[varName] = varType;
            if (varsDeclared.Contains(varName))
                return;
            varsDeclared.Add(varName);
        }

        //create a strategy parameter
        private void CreateParameter(string paramName, ParameterType pt, List<string> tokens)
        {
            //create parameter instance for tracking
            Parameter p = new Parameter();
            p.Type = pt;
            p.Name = paramName;
            _parameters[paramName] = p;

            //source parameters are not turned into parameters
            if (pt == ParameterType.PriceComponent)
            {
                DeclareVar(paramName, "TimeSeries");
                string tsType = tokens[4];
                string tsVal = "bars.Close";
                if (ohclv.Contains(tsType))
                    tsVal = "bars." + tsType.ToProper();
                string initLine = paramName + " = " + tsVal + ";";
                AddToInitializeMethod(initLine);
                return;
            }

            //create declaration statement
            DeclareVar(paramName, "Parameter");

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

        //remove tokens up to the specified end token
        private void RemoveTokensUpTo(List<string> tokens, string endToken, int startIdx, bool inclusive)
        {
            for(int n = startIdx; n < tokens.Count; n++)
            {
                if (tokens[n] == endToken)
                {
                    if (inclusive)
                        tokens.RemoveAt(n);
                    return;
                }
                else
                {
                    tokens.RemoveAt(n);
                    n--;
                }
            }
        }

        //extract tokens until a closing parenthesis is found
        private List<string> ExtractArgumentTokens(List<string> tokens, int idx)
        {
            int parens = 0;
            List<string> argTokens = new List<string>();
            while(idx < tokens.Count)
            {
                string token = tokens[idx];
                tokens.RemoveAt(idx);
                if (token == "(")
                    parens++;
                else if (token == ")")
                {
                    if (parens > 0)
                        parens--;
                    else
                        break;
                }
                argTokens.Add(token);
            }
            return argTokens;
        }

        //variables
        private bool indicatorMapped = false;
        private string ifAssignment = null;
        private string elseAssignment = null;
        private int recurse = 0;
        private int indentLevel = 0;
        private List<string> varsDeclared = new List<string>();
        private Dictionary<string, string> varTypes = new Dictionary<string, string>();
        private List<string> initializeBody = new List<string>();
        private List<string> executeBody = new List<string>();
        private List<string> varDecl = new List<string>();
        private List<string> timeSeriesVars = new List<string>();
        private List<string> usingClauses = new List<string>();
        private List<string> constructorBody = new List<string>();
        private static List<string> ohclv = new List<string>() { "open", "high", "low", "close", "volume" };
        private static List<string> mathOps = new List<string>() { "+", "-", "*", "/" };
        private static List<string> logicalOps = new List<string>() { ">", "<", ">=", "<=" };
        List<List<string>> indParams;
        private static Dictionary<string, string> pvIndicators = new Dictionary<string, string>() { { "ema", "EMA" }, { "rsi", "RSI" }, { "sma", "SMA" }, { "barssince", "BarsSince" },
            { "bbw", "BBWidth" }, { "cci", "CCI" }, { "cmo", "CMO" }, { "cog", "CG" }, { "correlation", "Corr" }, { "dev", "MeanAbsDev" } };
        private Dictionary<string, Parameter> _parameters = new Dictionary<string, Parameter>();
        private static List<string> taIndicators = new List<string>() { "ema", "sma", "rsi", "macd", "stoch", "atr", "adx", "adxdi", "dmi", "wma", "vwma", "hma", "cmo", "mom", "roc",
            "stdev", "variance", "highest", "lowest", "rma", "crossover", "crossunder" };
        private string paneTag = "Price";
        private List<string> prevComments = new List<string>();
        private bool ifStatement = false;
        private int dynamicVarCount = 1;
    }

    //current processing line mode
    public enum LineMode { Scalar, Series };
}