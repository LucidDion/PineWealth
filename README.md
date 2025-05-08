# PineWealth

**PineWealth** is a community-driven translator that converts [TradingView](https://tradingview.com)'s **Pine Script** strategies into **WealthLab 8 C# Strategies**.

> 🚀 Empower Pine Script users with WealthLab’s powerful backtesting and portfolio simulation engine.

---

## 🔍 What It Does

PineWealth reads Pine Script code and translates it into WealthLab 8-compatible C# strategy code. This allows traders to take their strategies from TradingView and leverage WealthLab’s advanced features like:

- Portfolio level backtesting with survivorship-bias free DataSets
- Advanced backtesting Visualizers
- Position sizing and risk control
- Optimization (parallel, walk-forward, symbol-by-symbol)

---

## ✨ Why This Exists

TradingView's scripting language is powerful and widely used, but its backtesting capabilities are limited. WealthLab 8 brings professional-grade performance testing, and PineWealth bridges the gap.

Use it to:

- Migrate strategies from TradingView to WealthLab
- Extend strategies using full C# flexibility
- Enhance your workflow and simulations

---

## ✅ Supported Features (So far)

- Basic indicator detection
- `long`/`short` entry/exit logic
- Variable declaration and assignments
- Auto-initialization of indicators in `Initialize()` method

---

## 🔜 Roadmap

- [ ] Support full set of indicators in ta library (WIP, about half complete)
- [x] Add support for MACD and other tuple-based indicators
- [ ] Support assignment of tuples directly to variable, then accessing tuple members later 
- [ ] Position sizing
- [x] Convert Inputs into Strategy Parameters
- [x] Translate plots

---

## 💻 How It Works

PineWealth uses a custom line-by-line parser with lightweight tokenization. It reads Pine Script lines and emits WealthLab-ready C# code. Indicator objects are declared and initialized properly in the `Initialize()` method.

> Example:  
> Pine Script  
> ```pinescript
> //@version=4
> strategy("RSI Overbought/Oversold", overlay=true)
> 
> x = 20
> rsi = ta.rsi(close, 14)
> [middle, upper, lower] = ta.bb(close, 20, 20)
> 
> if (rsi > 70)
>     strategy.entry("Short", strategy.short)
>     x++
> 
> if (rsi < 30)
>     strategy.entry("Long", strategy.long)
>     x--
> ```  
>  
> becomes WealthLab C#:
> ```csharp
> using WealthLab.Backtest;
> using System;
> using WealthLab.Core;
> using WealthLab.Data;
> using WealthLab.Indicators;
> using System.Collections.Generic;
> 
> namespace WealthScript1 
> {
>    public class MyStrategy : UserStrategyBase
>    {
>       //create indicators and other objects here, this is executed prior to the main trading loop
>       public override void Initialize(BarHistory bars)
>       {
>          rsi = RSI.Series ( bars.Close , 14 ) ;
>          middle = BBUpper.Series(bars.Close, 20, 20 );
>          upper = SMA.Series(bars.Close, 20 );
>          lower = BBLower.Series(bars.Close, 20, 20 );
> 
>       }
>        
>       //execute the strategy rules here, this is executed once for each bar in the backtest history
>       public override void Execute(BarHistory bars, int idx)
>       {
>          x = 20 ; 
>          if ( rsi[idx] > 70 ) 
>          {
>             if (HasOpenPosition(bars, PositionType.Long))
>                PlaceTrade(bars, TransactionType.Sell, OrderType.Market);
>             PlaceTrade(bars, TransactionType.Short, OrderType.Market, 0, "Short"); 
>             x ++ ; 
>          }
>          if ( rsi[idx] < 30 ) 
>          {
>             if (HasOpenPosition(bars, PositionType.Short))
>                PlaceTrade(bars, TransactionType.Cover, OrderType.Market);
>             PlaceTrade(bars, TransactionType.Buy, OrderType.Market, 0, "Long"); 
>             x -- ; 
>          }
> 
>       }
>        
>       //declare private variables below
>       private double x;
>       private RSI rsi;
>       private BBUpper middle;
>       private SMA upper;
>       private BBLower lower;
>    }
> }
> ```

---

## 🚀 Getting Started

```bash
git clone https://github.com/yourname/PineWealth.git
cd PineWealth
```
Or, download this repo in Visual Studio.
