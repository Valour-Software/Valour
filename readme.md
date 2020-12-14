# Valour

### Valour is an open-source, modern chat client which focuses on privacy and security while implementing features that bring it beyond the norm.
<br/>

## Design
<hr/>
Valour's messaging system is designed to keep you in control of your messages. When you send a message, it is cached for 24 hours on the Valour backend before being permanently deleted. What is kept is a hash of the message and its metadata.
<br/><br/>
This metadata allows a peer-to-peer subsystem to server messages that are not cached on the Valour backend. Users within communities can decide how many cached messages they would like to store to contribute to the server message integrity, and even select entire channels and servers to archive entirely.
<br/><br/>
With the peering system allowing for extended message history, and no permanent records of any of your messages, your data is your own and we cannot sell or view your data. However, due to the hashes, we can validate that messages sent though the P2P system are valid, ensuring that no fraud can occur on the system.