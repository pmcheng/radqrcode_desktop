# RadQRCode

## *Using QR Codes for recording and sharing teaching file metadata on mobile devices*
 
This is the desktop portion of an application set designed to facilitate recording case metadata for personal teaching files.

The desktop application allows drag and drop from a Synapse PACS workstation, automatically populating case metadata.  The user can add a free text description.  A QR code is generated dynamically from concatenated metadata text separated by a vertical bar delimiter.  The application also allows for direct retrieval of DICOM data from the Synapse server, as well as drag and drop of a DICOM file.

Requires .NET Framework 4.7.2 or higher.

This project uses code from the following open source projects:

* [Open source QRCode library from the Code Project](http://www.codeproject.com/Articles/20574/Open-Source-QRCode-Library)
    