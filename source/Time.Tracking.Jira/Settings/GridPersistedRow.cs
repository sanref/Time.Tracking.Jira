/**************************************************************************
Copyright 2016 Carsten Gehling

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

namespace Time.Tracking.Jira
{
    /// <summary>
    /// Una fila de la grilla tal como se persiste. Se guarda en JSON desde la 4.1; ya no lleva
    /// [Serializable] porque BinaryFormatter, el unico que lo miraba, dejo de existir.
    /// </summary>
    internal class GridPersistedRow
    {
        public string ParentIssue { get; set; }
        public string Subtask { get; set; }
        public bool TimerRunning { get; set; }
        public DateTimeOffset? InitialStartTime { get; set; }
        public DateTime SessionStartTime { get; set; }
        public TimeSpan TotalTime { get; set; }

        /// <summary>
        /// Fila fijada arriba de la lista. Ausente en los archivos anteriores a la 4.1.1, donde
        /// queda en false: ninguna fila estaba fijada todavia.
        /// </summary>
        public bool Pinned { get; set; }
    }
}
