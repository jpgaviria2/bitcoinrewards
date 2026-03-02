package com.bitcoinrewards.nfcdisplay.ndef;

/**
 * Minimal state holder for HCE — current message and selected file.
 */
public class NdefStateManager {
    private String messageToSend = "";
    private byte[] selectedFile = null;

    public String getMessageToSend() { return messageToSend; }
    public void setMessageToSend(String message) { this.messageToSend = message != null ? message : ""; }
    public byte[] getSelectedFile() { return selectedFile; }
    public void setSelectedFile(byte[] file) { this.selectedFile = file; }
}
