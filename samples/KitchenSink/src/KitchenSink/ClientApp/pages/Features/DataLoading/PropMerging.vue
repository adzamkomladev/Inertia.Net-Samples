<script setup lang="ts">
import { Head, router } from '@inertiajs/vue3';
import { computed } from 'vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

const props = defineProps<{
    notifications: Array<{ id: number; message: string; type: string }>;
    activities: Array<{
        id: number;
        action: string;
        subject: string;
        time: string;
    }>;
    contacts: Array<{ id: number; name: string; updated: string }>;
    timestamp: string;
}>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Data Loading' },
    { title: 'Prop Merging' },
];

function addNotification() {
    router.reload({ only: ['notifications', 'timestamp'] });
}

function resetNotifications() {
    router.reload({
        reset: ['notifications'],
        only: ['notifications', 'timestamp'],
    });
}

function addActivity() {
    router.reload({ only: ['activities', 'timestamp'] });
}

function resetActivities() {
    router.reload({ reset: ['activities'], only: ['activities', 'timestamp'] });
}

function fetchNextContact() {
    router.reload({ only: ['contacts', 'timestamp'] });
}

function resetContacts() {
    router.reload({ reset: ['contacts'], only: ['contacts', 'timestamp'] });
}

const latestContactTimestamp = computed(() => {
    if (!props.contacts.length) return null;
    return props.contacts.reduce(
        (latest, contact) =>
            contact.updated > latest ? contact.updated : latest,
        props.contacts[0].updated,
    );
});
</script>

<template>
    <Head title="Prop Merging" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Prop Merging"
                docs="data-props/merging-props"
                controller="app/Http/Controllers/Feature/DataLoadingController.php#L113"
            >
                Server-side merge strategies with
                <code class="text-xs">Inertia::merge()</code>. New items are
                appended instead of replacing during partial reloads.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- Notifications (merge) -->
                <FeatureCard info-card title="Notifications">
                    <template #header-action>
                        <Badge variant="secondary"
                            >{{ notifications.length }} total</Badge
                        >
                    </template>
                    <template #description>
                        <code class="text-xs">Inertia::merge()</code> appends
                        new items to the existing array on each partial reload.
                    </template>
                    <div class="space-y-3">
                        <div class="flex flex-wrap gap-2">
                            <Button size="sm" @click="addNotification">
                                Add Notification
                            </Button>
                            <Button
                                size="sm"
                                variant="outline"
                                @click="resetNotifications"
                            >
                                Reset
                            </Button>
                        </div>

                        <div v-if="notifications.length" class="space-y-2">
                            <div
                                v-for="notification in notifications"
                                :key="notification.id"
                                class="flex items-center justify-between rounded border border-black/10 px-3 py-2 dark:border-white/10"
                            >
                                <span class="text-sm">{{
                                    notification.message
                                }}</span>
                                <Badge
                                    :variant="
                                        notification.type === 'success'
                                            ? 'default'
                                            : notification.type === 'warning'
                                              ? 'destructive'
                                              : 'secondary'
                                    "
                                    class="text-xs"
                                >
                                    {{ notification.type }}
                                </Badge>
                            </div>
                        </div>
                        <p v-else class="text-sm text-muted-foreground">
                            Click "Add Notification" to start appending.
                        </p>
                    </div>
                </FeatureCard>

                <!-- Prepend (activity log) -->
                <FeatureCard info-card title="Prepend">
                    <template #header-action>
                        <Badge variant="secondary"
                            >{{ activities.length }} total</Badge
                        >
                    </template>
                    <template #description>
                        <code class="text-xs"
                            >Inertia::merge([...])->prepend()</code
                        >
                        adds new items to the beginning of the array.
                    </template>
                    <div class="space-y-3">
                        <div class="flex flex-wrap gap-2">
                            <Button size="sm" @click="addActivity">
                                Log Activity
                            </Button>
                            <Button
                                size="sm"
                                variant="outline"
                                @click="resetActivities"
                            >
                                Reset
                            </Button>
                        </div>

                        <div v-if="activities.length" class="space-y-2">
                            <div
                                v-for="activity in activities"
                                :key="activity.id"
                                class="flex items-center justify-between rounded border border-black/10 px-3 py-2 dark:border-white/10"
                            >
                                <span class="text-sm"
                                    >{{ activity.action }}
                                    {{ activity.subject }}</span
                                >
                                <Badge
                                    variant="outline"
                                    class="font-mono text-xs"
                                    >{{ activity.time }}</Badge
                                >
                            </div>
                        </div>
                        <p v-else class="text-sm text-muted-foreground">
                            Click "Log Activity" to start prepending.
                        </p>
                    </div>
                </FeatureCard>

                <!-- matchOn -->
                <FeatureCard info-card title="matchOn (Dedup by ID)">
                    <template #header-action>
                        <Badge variant="secondary"
                            >{{ contacts.length }} / 5 contacts</Badge
                        >
                    </template>
                    <template #description>
                        <code class="text-xs"
                            >Inertia::merge([...])->matchOn('id')</code
                        >. New items are appended and existing items are updated
                        in place, all in a single merge.
                    </template>
                    <div class="space-y-3">
                        <div class="flex flex-wrap gap-2">
                            <Button size="sm" @click="fetchNextContact">
                                Fetch Next Contact
                            </Button>
                            <Button
                                size="sm"
                                variant="outline"
                                @click="resetContacts"
                            >
                                Reset
                            </Button>
                        </div>

                        <div v-if="contacts.length" class="space-y-2">
                            <div
                                v-for="contact in contacts"
                                :key="contact.id"
                                class="flex items-center justify-between rounded px-3 py-2"
                                :class="
                                    contact.updated === latestContactTimestamp
                                        ? 'border border-blue-200 bg-blue-50/50 dark:border-blue-800 dark:bg-blue-950/50'
                                        : 'border border-black/10 dark:border-white/10'
                                "
                            >
                                <div>
                                    <span class="text-sm font-medium">{{
                                        contact.name
                                    }}</span>
                                    <span
                                        class="ml-2 text-xs text-muted-foreground"
                                        >ID: {{ contact.id }}</span
                                    >
                                </div>
                                <Badge
                                    variant="outline"
                                    class="font-mono text-xs"
                                    >{{ contact.updated }}</Badge
                                >
                            </div>
                        </div>
                        <p v-else class="text-sm text-muted-foreground">
                            Click "Fetch Next Contact" to start loading.
                        </p>

                        <div
                            class="rounded-lg border border-black/5 bg-neutral-50/80 p-3 font-mono text-xs dark:border-white/5 dark:bg-neutral-900/80"
                        >
                            <p>
                                Each fetch returns all known contacts with fresh
                                timestamps plus one new contact.
                                <code>matchOn('id')</code> handles both at once:
                                new IDs are appended, existing IDs are updated
                                in place (blue highlight). After 5 fetches the
                                pool is full, so every fetch only updates.
                            </p>
                        </div>
                    </div>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
