// The subset of Wayfinder's generated model and page prop types this demo uses.
// Server props keep Laravel's snake_case names so the upstream UI can be used unchanged.
export namespace App {
    export namespace Models {
        export type User = {
            id: number;
            name: string;
            email: string;
            email_verified_at: string | null;
            created_at: string | null;
            updated_at: string | null;
        };

        export type Organization = {
            id: number;
            name: string;
            contacts_count?: number;
            created_at: string | null;
        };

        export type Contact = {
            id: number;
            first_name: string;
            last_name: string;
            email: string | null;
            phone: string | null;
            organization?: Organization | null;
            is_favorite: boolean;
            created_at: string;
        };

        export type Note = {
            id: number;
            body: string;
            contact?: Contact;
            user?: User;
            created_at: string;
        };
    }
}

export namespace Inertia {
    export namespace Pages {
        export namespace Crm {
            export type Dashboard = {
                totalContacts?: number;
                totalOrganizations?: number;
                recentNotesCount?: number;
                recentActivity: App.Models.Note[];
            };
        }

        export namespace Contacts {
            export type Index = {
                contacts: unknown;
                filters: { search: string; favorite: boolean };
            };

            export type Create = {
                organizations: App.Models.Organization[];
            };

            export type Edit = {
                contact: App.Models.Contact;
                organizations: App.Models.Organization[];
            };

            export type Show = {
                contact: App.Models.Contact;
                notes?: App.Models.Note[];
            };
        }

        export namespace Organizations {
            export type Index = {
                organizations: unknown;
                filters: { search: string };
            };

            export type Show = {
                organization: App.Models.Organization;
                contacts: unknown;
            };
        }

        export namespace Features {
            export namespace Forms {
                export type Wayfinder = {
                    sampleContact: App.Models.Contact;
                };
            }
        }
    }
}
