namespace NzbDrone.Core.Update
{
    // The key update manifests are signed with: the maintainer's, which also signs release tags. The
    // release workflow never holds it (ADR 0024). build/update-signers names the same key for
    // ssh-keygen. A new key ships in a release before anything is signed with it.
    public static class UpdateSigningKey
    {
        public const string Namespace = "nodearr-update";

        public const string PublicKey = "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAACAQCciM+HleZQQ+DTFM6H6hb4Dh6/XlXpy7hC0mWxkAc1iAuWZZlCXMYMRREvrU/nw84wKcA7igyFiz6iEry4RmLOnUJdzL7p+4e2qCCK5azNx4mY1W0FP+xUVN4LIXpkTfxfCV5F3furVl7ECTbGC3gzfqwjfL1CybpGwdNk4AAY1zYrx3ZyeK2vwwqhB8gnGlnhFKkBK39mqOo2SQTPPdwuHmlwS6ebNJnraKlSxDnq5HiA+KWTkveOVTfZJ4pEgRgJFEMU+GRSaGeQPHwNSbOCEYfBA4iKeAaZF5x5/fV6g0Wql8w3zzrBhK2yMgKTNw63gbxXervJ8qDeDGvpEucy33HIR/HAvs24pRrsUVYahbCR0D63in6+TjGO84Qba6T4xrjQ0QOqVXVHYnaWROnrIsSdTJ9RD91A3PYaEA0dr4pE7oJLtdBOZrC7GL5xkeIK0JaThk8MZn2h5N5MFPa9Zta212chGh6PD6YFNEy5bGxFVskb9u0FZjwloYEXFK1o0Sy2NBSVWXeFMdibWg0l9YjpEi73r4/ed0fPj0FyJfC7GdH89jR2/zzAnG6g7YwFSXyRWHWnVNN23M4UqmNyZsYN+NYmiURLoV0CaO8hWBsXyYIKci6GKogXRx/SacjDJrDorYvmCHPe8YZm7gn9vQPsLYoS8QGdLoS8H7wqaw==";
    }
}
