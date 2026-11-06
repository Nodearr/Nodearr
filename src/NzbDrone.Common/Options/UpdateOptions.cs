namespace NzbDrone.Common.Options;

public class UpdateOptions
{
    public string Mechanism { get; set; }
    public bool? Automatically { get; set; }
    public string ScriptPath { get; set; }
    public string Branch { get; set; }

    // Nodearr__Update__Check: whether to ask updates.nodearr.com for the newest version.
    public bool? Check { get; set; }

    // For testing, and only in the environment: the folder the manifest is fetched from, in place
    // of https://updates.nodearr.com/v1/, and the SSH public key it must be signed with, in place of
    // the one built in. Whoever sets the environment can replace the program anyway.
    public string ManifestUrl { get; set; }
    public string PublicKey { get; set; }
}
