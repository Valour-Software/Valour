using System;
using System.IO;
using System.Text;
namespace json_api
{
    class api
    {
        static void Main()
        {
            string jsonName = @"monke.json"
            string path = Path.getFullPath(jsonName); //the absolute path for the json file
        }
        static string jsonify(string stringToJsonify = "", bool makeFile = false, string fileNameWithPath = "")
        {
            /* uses key-value like json, accepts null does accept itself
            * example:
            * api.jsonify("num:123,string:'value2', nullValue: null) ->(string) {\n key1: value1,\n key2: value2}
            * obs: spaces are not ignored
            */

            string toReturn = "";
            if (!makeFile)
            {
                if (stringToJsonify.Length == 0)
                {
                    return "{\n}";
                }
                else
                {
                    toReturn += "{\n \"";
                    foreach (char charAt in stringToJsonify)
                    {
                        if (":".Contains(charAt) || "=".Contains(charAt))
                        {
                            toReturn += "\": ";
                            continue;
                        }
                        else if (",".Contains(charAt))
                        {
                            toReturn += ", \n \"";
                            continue;
                        }
                        else if ("'".Contains(charAt))
                        {
                            toReturn += "\"";
                            continue;
                        }
                        else
                        {
                            toReturn += charAt;
                            continue;
                        }

                    }
                    toReturn += "\n}";
                    return toReturn;
                }
            }
            else
            {
                if (stringToJsonify.Length == 0)
                {
                    toReturn = "{\n}";
                }
                else
                {
                    toReturn += "{\n \"";
                    foreach (char charAt in stringToJsonify)
                    {
                        if (":".Contains(charAt) || "=".Contains(charAt))
                        {
                            toReturn += "\": ";
                            continue;
                        }
                        else if (",".Contains(charAt))
                        {
                            toReturn += ", \n \"";
                            continue;
                        }
                        else if ("'".Contains(charAt))
                        {
                            toReturn += "\"";
                            continue;
                        }
                        else
                        {
                            toReturn += charAt;
                            continue;
                        }

                    }
                    toReturn += "\n}";

                }
                writeJson(fileNameWithPath, toReturn);
                return toReturn;
            }
        }

        static void writeJson(string filename, string absPath, string content)
        {
            // nothing to worry too much with this one
            if (filename.Contains(".json"))
            {
                System.IO.File.WriteAllText($"{absPath}\\{filename}", content);
            }
            else
            {
                System.IO.File.WriteAllText($"{absPath}\\{filename}.json", content);
            }

        }

        static void writeJson(string fileNameWithPath, string content)
        {
            // nothing to worry too much with this one too, they are the same (almost)
            if (fileNameWithPath.Contains(".json"))
            {
                System.IO.File.WriteAllText($"{fileNameWithPath}", content);
            }
            else
            {
                System.IO.File.WriteAllText($"{fileNameWithPath}.json", content);
            }

        }

        static string[] readJson(string filenameWithPath, int get)
        {
            /*
            * get = 1 -> return keys
            * get = 2 -> return values
            * get = 3 -> return all
            {\n key1: value1,\n key2: value2}
            */
            string jsonFile = "";
            if (filenameWithPath.Contains(".json")){
                jsonFile = System.IO.File.ReadAllText($"{filenameWithPath}");
            } else {
                jsonFile = System.IO.File.ReadAllText($"{filenameWithPath}.json");
            }
            string[] jsonFinal = {};
            string subJsonValue = "";
            if (get == 1) {
                bool getValue = false;
                bool skipNext = false;
                foreach (char charAt in jsonFile)
                {
                    if (skipNext)
                    {
                        skipNext = false;
                        continue;
                    }

                    if (":".Contains(charAt))
                    {
                        getValue = false;
                        skipNext = true;
                        jsonFinal = Add(jsonFinal, subJsonValue);
                        subJsonValue = "";
                        continue;
                    }
                    if (getValue){
                        subJsonValue += charAt;
                        continue;
                    }

                    else if (",\n".Contains(charAt) && !getValue)
                    {
                        getValue = true;
                        skipNext = true;
                        continue;
                    }
                }
                return jsonFinal;
            } else if (get == 2) {

                bool getValue = false;
                bool skipNext = false;
                foreach (char charAt in jsonFile)
                {
                    if (skipNext)
                    {
                        skipNext = false;
                        continue;
                    }

                    if (",\n".Contains(charAt))
                    {
                        getValue = false;
                        skipNext = true;
                        jsonFinal = Add(jsonFinal, subJsonValue);
                        subJsonValue = "";
                        continue;
                    }
                    if (getValue){
                        subJsonValue += charAt;
                    }

                    else if (":".Contains(charAt) && !getValue)
                    {
                        getValue = true;
                        skipNext = true;
                    }
                }
                return jsonFinal;
            }
            else
            {
                jsonFinal = jsonFile.Split("\n");
            }

            return jsonFinal;


        }

        static string[] Add(string[] array, string newValue){
            int newLength = array.Length + 1;

            string[] result = new string[newLength];

            for(int i = 0; i < array.Length; i++)
                result[i] = array[i];

            result[newLength -1] = newValue;

            return result;
        }

        static string[] RemoveAt(string[] array, int index){
            int newLength = array.Length - 1;

            if(newLength < 1)
            {
                return array;
            }

            string[] result = new string[newLength];
            int newCounter = 0;
            for(int i = 0; i < array.Length; i++)
            {
                if(i == index)
                {
                    continue;
                }
                result[newCounter] = array[i];
                newCounter++;
            }

            return result;
        }
    }

}
