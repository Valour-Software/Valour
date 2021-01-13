import json
from os.path import abspath
############################################################
#
# TODO: make these functions async
# TODO: resolve path problem
# TODO: use on... functions in awake.cs
#
############################################################


class Api:
    def __init__(self):
        self.json = {}

    def get_json(self, filepath=None, tokenize=True):
        if filepath is None:
            filepath = abspath("api.json")
            with open(filepath, "r") as file:
                JSON = json.load(file)
        if tokenize:
            Api().tokenize(JSON)
        self.json = json
        return JSON

    def set_json(self, json_obj: dict, name="api.json", filepath=None, tokenize=True):

        if filepath is None:
            filepath = abspath("api.json")

            with open(f"{filepath}\\{name}.json", "r") as file:
                json.dump(Api().tokenize(json_obj), file)

    def tokenize(self, json: dict):
        """

        :param json: the dictionary of the json file.
        :return: the same dict, but the first key is the action needed to do.
        to create another action, use:
        sorted(json.keys()) == sorted([<<keys here>>])
        because comparing with sorted lists is faster than counting.
        """

        if sorted(json.keys()) == sorted(["author", "date", "System/planet", "messageSent"]):
            action = "messageSent"
        elif set(json.keys()) == sorted(["user", "date", "System/planet"]):
            action = "userJoin"
        else:  # base case
            action = "quiet"
        self.json = json
        json.update({"action": action})
        return json
