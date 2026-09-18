/**************************************************************************
Copyright 2026 fnas (based in Jira StopWatch by Carsten Gehling)

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
**************************************************************************/
using System;
using System.Collections.Generic;
using System.Formats.Nrbf;
using System.IO;

namespace Time.Tracking.Jira
{
    /// <summary>
    /// Lee las listas que las versiones sobre .NET Framework dejaron guardadas en base64 con
    /// BinaryFormatter (los settings <c>GridPersistedRows</c> y <c>PersistedIssues</c>).
    /// </summary>
    /// <remarks>
    /// BinaryFormatter ya no existe en .NET, pero su formato binario esta especificado
    /// (MS-NRBF) y <see cref="NrbfDecoder"/> lo decodifica sin instanciar ningun tipo: solo
    /// devuelve registros con nombres de miembro y valores. Eso es todo lo que hace falta para
    /// migrar la configuracion, y ademas evita el problema de seguridad que hizo que
    /// BinaryFormatter se retirara, porque aca nunca se construye un objeto arbitrario.
    /// Viene dentro de Microsoft.WindowsDesktop.App, asi que no agrega ninguna dependencia.
    ///
    /// Solo lectura, a proposito: lo nuevo se guarda en JSON (ver <see cref="SettingsStore"/>).
    /// Este codigo existe unicamente para el camino de migracion y puede borrarse cuando ya
    /// no queden instalaciones anteriores a la 4.1.
    /// </remarks>
    internal static class NrbfReader
    {
        /// <summary>
        /// Los elementos de un <c>List&lt;T&gt;</c> serializado. Lista vacia si el texto no es
        /// base64 valido, si no es un payload NRBF o si no tiene la forma esperada: en una
        /// migracion conviene perder las filas antes que impedir que la aplicacion arranque.
        /// </summary>
        internal static List<ClassRecord> ReadListItems(string base64)
        {
            List<ClassRecord> items = new List<ClassRecord>();

            if (string.IsNullOrEmpty(base64))
                return items;

            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64); }
            catch (FormatException) { return items; }

            ClassRecord list;
            try
            {
                using (MemoryStream ms = new MemoryStream(bytes))
                    list = NrbfDecoder.Decode(ms) as ClassRecord;
            }
            catch (Exception)
            {
                // SerializationException y varias de IO: cualquiera significa lo mismo aca,
                // que el blob no se puede aprovechar.
                return items;
            }

            if (list == null)
                return items;

            SZArrayRecord<SerializationRecord> array =
                list.GetRawValue("_items") as SZArrayRecord<SerializationRecord>;

            if (array == null)
                return items;

            // List<T> sobreasigna su arreglo interno, asi que _items trae mas posiciones que
            // elementos reales; las sobrantes son basura. _size dice donde termina lo valido.
            int size = list.GetRawValue("_size") as int? ?? 0;
            SerializationRecord[] elements = array.GetArray();

            for (int i = 0; i < size && i < elements.Length; i++)
            {
                ClassRecord element = elements[i] as ClassRecord;
                if (element != null)
                    items.Add(element);
            }

            return items;
        }

        /// <summary>
        /// Nombre real del campo que respalda una propiedad automatica. BinaryFormatter
        /// serializaba campos, no propiedades, y el compilador los llama asi.
        /// </summary>
        private static string BackingField(string propertyName)
        {
            return string.Format("<{0}>k__BackingField", propertyName);
        }

        internal static string GetString(ClassRecord record, string propertyName)
        {
            return record.GetRawValue(BackingField(propertyName)) as string ?? "";
        }

        internal static bool GetBoolean(ClassRecord record, string propertyName)
        {
            return record.GetRawValue(BackingField(propertyName)) as bool? ?? false;
        }

        internal static DateTime GetDateTime(ClassRecord record, string propertyName)
        {
            return record.GetRawValue(BackingField(propertyName)) as DateTime? ?? default(DateTime);
        }

        internal static TimeSpan GetTimeSpan(ClassRecord record, string propertyName)
        {
            return record.GetRawValue(BackingField(propertyName)) as TimeSpan? ?? TimeSpan.Zero;
        }

        /// <summary>
        /// Un <c>DateTimeOffset?</c>. Sin valor viene como null; con valor, como un registro
        /// anidado.
        /// </summary>
        /// <remarks>
        /// DateTimeOffset se serializa via ISerializable con los nombres "DateTime" y
        /// "OffsetMinutes", donde el primero es la hora de reloj del offset, no UTC. Por eso
        /// se fuerza Kind=Unspecified: es lo que espera el constructor y lo que evita que un
        /// Kind heredado (Utc o Local) haga fallar la conversion cuando el offset no coincide.
        /// </remarks>
        internal static DateTimeOffset? GetDateTimeOffset(ClassRecord record, string propertyName)
        {
            ClassRecord nested = record.GetRawValue(BackingField(propertyName)) as ClassRecord;
            if (nested == null)
                return null;

            DateTime? clock = nested.GetRawValue("DateTime") as DateTime?;
            if (clock == null)
                return null;

            short offsetMinutes = nested.GetRawValue("OffsetMinutes") as short? ?? 0;

            try
            {
                return new DateTimeOffset(
                    DateTime.SpecifyKind(clock.Value, DateTimeKind.Unspecified),
                    TimeSpan.FromMinutes(offsetMinutes));
            }
            catch (ArgumentException)
            {
                // Offset fuera de rango o fecha limite: la fila sigue sirviendo sin esto.
                return null;
            }
        }
    }
}
