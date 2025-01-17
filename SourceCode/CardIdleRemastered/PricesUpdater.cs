﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using CardIdleRemastered.Badges;
using Newtonsoft.Json;

namespace CardIdleRemastered
{
    public class PricesUpdater
    {
        private Dictionary<string, BadgeStockModel> _prices;

        public FileStorage Storage { get; set; }

        public Dictionary<string, BadgeStockModel> Prices
        {
            get
            {
                if (_prices == null)
                {
                    var values = Storage.ReadContent();
                    _prices = JsonConvert.DeserializeObject<Dictionary<string, BadgeStockModel>>(values);
                }
                return _prices;
            }
        }

        public async Task<bool> DownloadCatalog()
        {
            try
            {
                string values = await SteamParser.DownloadString("http://api.steamcardexchange.net/GetBadgePrices.json");
                Storage.WriteContent(values);
                _prices = null;
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "");
                return false;
            }
            return true;
        }

        public BadgeStockModel GetStockModel(string id)
        {
            BadgeStockModel b = null;
            Prices?.TryGetValue(id, out b);
            return b;
        }
    }
}
