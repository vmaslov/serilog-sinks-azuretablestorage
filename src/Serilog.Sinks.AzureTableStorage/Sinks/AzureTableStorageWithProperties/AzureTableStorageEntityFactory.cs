﻿// Copyright 2014 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Microsoft.WindowsAzure.Storage.Table;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Serilog.Sinks.AzureTableStorage.KeyGenerator;

namespace Serilog.Sinks.AzureTableStorage
{
    /// <summary>
    /// Utility class for Azure Storage Table entity
    /// </summary>
    public static class AzureTableStorageEntityFactory
    {
        // Azure tables support a maximum of 255 properties. PartitionKey, RowKey and Timestamp
        // bring the maximum to 252.
        const int _maxNumberOfPropertiesPerRow = 252;

        /// <summary>
        /// Creates a DynamicTableEntity for Azure Storage, given a Serilog <see cref="LogEvent"/>.Properties
        /// are stored as separate columns.
        /// </summary>
        /// <param name="logEvent">The event to log</param>
        /// <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>
        /// <param name="additionalRowKeyPostfix">Additional postfix string that will be appended to row keys</param>
        /// <param name="keyGenerator">The IKeyGenerator for the PartitionKey and RowKey</param>
        /// <param name="propertyColumns">Specific properties to be written to columns. By default, all properties will be written to columns.</param>
        /// <param name="saveMessageFields">Save additional (unnecessary) fields - MessageTemplate and RenderedMessage.</param>
        /// <returns></returns>
        public static DynamicTableEntity CreateEntityWithProperties(LogEvent logEvent, IFormatProvider formatProvider, string additionalRowKeyPostfix, IKeyGenerator keyGenerator, string[] propertyColumns = null, bool saveMessageFields = true)
        {
            var tableEntity = new DynamicTableEntity
            {
                PartitionKey = keyGenerator.GeneratePartitionKey(logEvent),
                RowKey = keyGenerator.GenerateRowKey(logEvent, additionalRowKeyPostfix),
                Timestamp = logEvent.Timestamp
            };

            var dynamicProperties = tableEntity.Properties;

            if (saveMessageFields)
            {
                dynamicProperties.Add("MessageTemplate", new EntityProperty(logEvent.MessageTemplate.Text));
                dynamicProperties.Add("RenderedMessage", new EntityProperty(logEvent.RenderMessage(formatProvider)));
            }

            dynamicProperties.Add("Level", new EntityProperty(logEvent.Level.ToString()));

            if (logEvent.Exception != null)
            {
                dynamicProperties.Add("Exception", new EntityProperty(logEvent.Exception.ToString()));
            }

            List<KeyValuePair<ScalarValue, LogEventPropertyValue>> additionalData = null;
            var count = dynamicProperties.Count;
            bool isValid;

            foreach (var logProperty in logEvent.Properties)
            {
                isValid = IsValidColumnName(logProperty.Key) && ShouldIncludeProperty(logProperty.Key, propertyColumns);

                // Don't add table properties for numeric property names
                if (isValid && (count++ < _maxNumberOfPropertiesPerRow - 1))
                {
                    dynamicProperties.Add(logProperty.Key, AzurePropertyFormatter.ToEntityProperty(logProperty.Value, null, formatProvider));
                }
                else
                {
                    if (additionalData == null)
                    {
                        additionalData = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>();
                    }
                    additionalData.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue(logProperty.Key), logProperty.Value));
                }
            }

            if (additionalData != null)
            {
                dynamicProperties.Add("AggregatedProperties", AzurePropertyFormatter.ToEntityProperty(new DictionaryValue(additionalData), null, formatProvider));
            }

            return tableEntity;
        }

        /// <summary>
        /// Determines whether or not the given property names conforms to naming rules for C# identifiers
        /// </summary>
        /// <param name="propertyName">Name of the property to check</param>
        /// <returns>true if the property name conforms to C# identifier naming rules and can therefore be added as a table property</returns>
        private static bool IsValidColumnName(string propertyName)
        {
            string regex = @"^(?:((?!\d)\w+(?:\.(?!\d)\w+)*)\.)?((?!\d)\w+)$";
            bool isValid = Regex.Match(propertyName, regex).Success;

            return isValid;
        }

        /// <summary>
        /// Determines if the given property name exists in the specific columns. 
        /// Note: If specific columns is not defined then the property name is considered valid. 
        /// </summary>
        /// <param name="propertyName">Name of the property to check</param>
        /// <param name="propertyColumns">List of defined properties only to be added as columns</param>
        /// <returns>true if the no propertyColumns are specified or it is included in the propertyColumns property</returns>
        private static bool ShouldIncludeProperty(string propertyName, string[] propertyColumns)
        {
            return propertyColumns == null || propertyColumns.Contains(propertyName);
        }
    }
}