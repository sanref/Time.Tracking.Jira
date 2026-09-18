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
// One property per field. Until 4.1.4 each of these carried a lower case and a pascal case
// property over the same backing field, because RestSharp's deserializer matched by exact
// name. System.Text.Json is configured case-insensitive (see JiraHttpClient.JsonOptions),
// which makes the pair a duplicate it would refuse to bind.

namespace Time.Tracking.Jira
{
    internal class Issue
    {
        public string Key { get; set; }

        public IssueFields Fields { get; set; }

        public ParentFields Parent { get; set; }
    }
}
