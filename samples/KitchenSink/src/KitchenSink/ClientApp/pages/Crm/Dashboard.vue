<script setup lang="ts">
import { Deferred, Head, Link, usePoll } from '@inertiajs/vue3';
import {
    Card,
    CardContent,
    CardDescription,
    CardHeader,
    CardTitle,
} from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';
import { dashboard } from '@/wayfinder/routes';
import contacts from '@/wayfinder/routes/contacts';
import type { Inertia } from '@/wayfinder/types';

defineProps<
    Omit<
        Inertia.Pages.Crm.Dashboard,
        'totalContacts' | 'totalOrganizations' | 'recentNotesCount'
    > & {
        totalContacts?: number;
        totalOrganizations?: number;
        recentNotesCount?: number;
    }
>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Dashboard', href: dashboard().url },
];

usePoll(30000, { only: ['recentActivity'] });
</script>

<template>
    <Head title="Dashboard" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <!-- Stats Cards -->
            <div class="grid gap-4 md:grid-cols-3">
                <Card>
                    <CardHeader>
                        <CardTitle class="text-sm font-medium"
                            >Total Contacts</CardTitle
                        >
                    </CardHeader>
                    <CardContent>
                        <Deferred data="totalContacts">
                            <template #fallback>
                                <Skeleton class="h-9 w-20" />
                            </template>
                            <div class="text-3xl font-semibold tracking-tight">
                                {{ totalContacts }}
                            </div>
                        </Deferred>
                    </CardContent>
                </Card>

                <Card>
                    <CardHeader>
                        <CardTitle class="text-sm font-medium"
                            >Organizations</CardTitle
                        >
                    </CardHeader>
                    <CardContent>
                        <Deferred data="totalOrganizations">
                            <template #fallback>
                                <Skeleton class="h-9 w-20" />
                            </template>
                            <div class="text-3xl font-semibold tracking-tight">
                                {{ totalOrganizations }}
                            </div>
                        </Deferred>
                    </CardContent>
                </Card>

                <Card>
                    <CardHeader>
                        <CardTitle class="text-sm font-medium"
                            >Notes This Week</CardTitle
                        >
                    </CardHeader>
                    <CardContent>
                        <Deferred data="recentNotesCount">
                            <template #fallback>
                                <Skeleton class="h-9 w-20" />
                            </template>
                            <div class="text-3xl font-semibold tracking-tight">
                                {{ recentNotesCount }}
                            </div>
                        </Deferred>
                    </CardContent>
                </Card>
            </div>

            <!-- Recent Activity -->
            <Card>
                <CardHeader>
                    <CardTitle>Recent Activity</CardTitle>
                    <CardDescription
                        >Latest notes added across all contacts</CardDescription
                    >
                </CardHeader>
                <CardContent>
                    <div
                        v-if="recentActivity.length === 0"
                        class="py-4 text-center text-sm text-muted-foreground"
                    >
                        No recent activity.
                    </div>
                    <div v-else class="space-y-2">
                        <div
                            v-for="note in recentActivity"
                            :key="note.id"
                            class="flex items-start gap-4 rounded-lg bg-muted/30 px-4 py-3"
                        >
                            <div class="flex-1 space-y-1">
                                <div class="flex items-center gap-2">
                                    <span class="text-sm font-medium">{{
                                        note.user?.name
                                    }}</span>
                                    <span class="text-xs text-muted-foreground"
                                        >added a note on</span
                                    >
                                    <Link
                                        v-if="note.contact"
                                        :href="
                                            contacts.show(note.contact.id).url
                                        "
                                        class="text-sm font-medium text-primary hover:underline"
                                    >
                                        {{ note.contact.first_name }}
                                        {{ note.contact.last_name }}
                                    </Link>
                                </div>
                                <p
                                    class="line-clamp-2 text-sm text-muted-foreground"
                                >
                                    {{ note.body }}
                                </p>
                            </div>
                            <time
                                class="text-xs whitespace-nowrap text-muted-foreground"
                            >
                                {{
                                    new Date(
                                        note.created_at,
                                    ).toLocaleDateString()
                                }}
                            </time>
                        </div>
                    </div>
                </CardContent>
            </Card>
        </div>
    </AppLayout>
</template>
