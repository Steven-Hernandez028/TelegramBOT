using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramBot
{
    public class Helper
    {
        /// <summary>
        /// Calcula el precio de liquidación para una operación en corto (SHORT).
        /// </summary>
        public static decimal CalculateShortLiquidationPrice(decimal entryPrice, decimal margin, decimal leverage)
        {
            decimal positionValue = margin * leverage;
            decimal numberOfCoins = positionValue / entryPrice;
            decimal numerator = (entryPrice * numberOfCoins) + (positionValue);
            decimal denominator = numberOfCoins + (margin / entryPrice);
            return numerator / denominator;
        }

        /// <summary>
        /// Calcula el precio de liquidación para una operación en largo (LONG).
        /// </summary>
        public static decimal CalculateLongLiquidationPrice(decimal entryPrice, decimal margin, decimal leverage)
        {
            decimal positionValue = margin * leverage;
            decimal numberOfCoins = positionValue / entryPrice;
            decimal numerator = (entryPrice * numberOfCoins) - margin;
            decimal denominator = numberOfCoins;
            return numerator / denominator;
        }

        /// <summary>
        /// Calcula el precio de liquidación aproximado para SHORT usando la fórmula simplificada.
        /// </summary>
        public static double CalculateShortLiquidationPriceSimplified(double entryPrice, double leverage)
        {
            return entryPrice * (1 + (1 / leverage));
        }

        /// <summary>
        /// Calcula el precio de liquidación aproximado para LONG usando la fórmula simplificada.
        /// </summary>
        public static double CalculateLongLiquidationPriceSimplified(double entryPrice, double leverage)
        {
            return entryPrice * (1 - (1 / leverage));
        }
    }
}
