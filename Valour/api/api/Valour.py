import Valour.api.api as valour


class Valour(valour.Api):
    def __init__(self):
        super(Valour, self).__init__()
        self.hash = None

    def onMessageSent(self):
        pass

    def onUserJoin(self):
        pass