using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramBot
{
    public class Helper
    {
       
        public static decimal CalculateShortLiquidationPrice(decimal entryPrice, decimal margin, decimal leverage)
        {
            decimal positionValue = margin * leverage;
            decimal numberOfCoins = positionValue / entryPrice;
            decimal numerator = (entryPrice * numberOfCoins) + (positionValue);
            decimal denominator = numberOfCoins + (margin / entryPrice);
            return numerator / denominator;
        }

        public static decimal CalculateLongLiquidationPrice(decimal entryPrice, decimal margin, decimal leverage)
        {
            decimal positionValue = margin * leverage;
            decimal numberOfCoins = positionValue / entryPrice;
            decimal numerator = (entryPrice * numberOfCoins) - margin;
            decimal denominator = numberOfCoins;
            return numerator / denominator;
        }

   
        public static double CalculateShortLiquidationPriceSimplified(double entryPrice, double leverage)
        {
            return entryPrice * (1 + (1 / leverage));
        }

     
        public static double CalculateLongLiquidationPriceSimplified(double entryPrice, double leverage)
        {
            return entryPrice * (1 - (1 / leverage));
        }
    }
}
