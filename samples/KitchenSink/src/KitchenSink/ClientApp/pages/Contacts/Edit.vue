<script setup lang="ts">
import { Form, Head } from '@inertiajs/vue3';
import InputError from '@/components/InputError.vue';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';
import contacts from '@/wayfinder/routes/contacts';
import type { Inertia } from '@/wayfinder/types';

const props = defineProps<Inertia.Pages.Contacts.Edit>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'CRM' },
    { title: 'Contacts', href: contacts.index().url },
    {
        title: `${props.contact.first_name} ${props.contact.last_name}`,
        href: contacts.show(props.contact.id).url,
    },
    { title: 'Edit' },
];
</script>

<template>
    <Head :title="`Edit ${contact.first_name} ${contact.last_name}`" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-4 p-4">
            <h1 class="text-2xl font-semibold tracking-tight">Edit Contact</h1>

            <Card class="max-w-2xl">
                <CardHeader>
                    <CardTitle>Contact Information</CardTitle>
                </CardHeader>
                <CardContent>
                    <Form
                        :action="contacts.update(contact.id).url"
                        method="put"
                        set-defaults-on-success
                        class="space-y-4"
                        #default="{
                            errors,
                            processing,
                            isDirty,
                            recentlySuccessful,
                        }"
                    >
                        <div class="grid gap-4 sm:grid-cols-2">
                            <div class="space-y-2">
                                <Label for="first_name">First Name</Label>
                                <Input
                                    id="first_name"
                                    name="first_name"
                                    :default-value="contact.first_name"
                                />
                                <InputError :message="errors.first_name" />
                            </div>
                            <div class="space-y-2">
                                <Label for="last_name">Last Name</Label>
                                <Input
                                    id="last_name"
                                    name="last_name"
                                    :default-value="contact.last_name"
                                />
                                <InputError :message="errors.last_name" />
                            </div>
                        </div>

                        <div class="grid gap-4 sm:grid-cols-2">
                            <div class="space-y-2">
                                <Label for="email">Email</Label>
                                <Input
                                    id="email"
                                    type="email"
                                    name="email"
                                    :default-value="contact.email ?? ''"
                                />
                                <InputError :message="errors.email" />
                            </div>
                            <div class="space-y-2">
                                <Label for="phone">Phone</Label>
                                <Input
                                    id="phone"
                                    name="phone"
                                    :default-value="contact.phone ?? ''"
                                />
                                <InputError :message="errors.phone" />
                            </div>
                        </div>

                        <div class="space-y-2">
                            <Label for="organization_id">Organization</Label>
                            <select
                                id="organization_id"
                                name="organization_id"
                                :value="contact.organization?.id ?? ''"
                                class="flex h-9 w-full rounded-md border border-input/60 bg-background px-3 py-1 text-sm placeholder:text-muted-foreground focus-visible:ring-1 focus-visible:ring-ring focus-visible:outline-none"
                            >
                                <option value="">None</option>
                                <option
                                    v-for="org in organizations"
                                    :key="org.id"
                                    :value="org.id"
                                >
                                    {{ org.name }}
                                </option>
                            </select>
                            <InputError :message="errors.organization_id" />
                        </div>

                        <div class="flex items-center gap-3 pt-2">
                            <Button type="submit" :disabled="processing">
                                {{ processing ? 'Saving...' : 'Save Changes' }}
                            </Button>
                            <span
                                v-if="isDirty"
                                class="text-sm text-muted-foreground"
                                >Unsaved changes</span
                            >
                            <span
                                v-if="recentlySuccessful"
                                class="text-sm text-green-600"
                                >Saved!</span
                            >
                        </div>
                    </Form>
                </CardContent>
            </Card>
        </div>
    </AppLayout>
</template>
