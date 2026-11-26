<script setup lang="ts">
import { Head, router } from '@inertiajs/vue3';
import { ref } from 'vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

defineProps<{
    users: Array<{ id: number; name: string; role: string }>;
    stats: { total: number; favorites: number };
    timestamp: string;
    randomNumber: number;
}>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Data Loading' },
    { title: 'Partial Reloads' },
];

const eventLog = ref<string[]>([]);

function log(message: string) {
    eventLog.value.unshift(`${new Date().toLocaleTimeString()} - ${message}`);
    if (eventLog.value.length > 10) eventLog.value.pop();
}
</script>

<template>
    <Head title="Partial Reloads" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Partial Reloads"
                docs="data-props/partial-reloads"
                controller="app/Http/Controllers/Feature/DataLoadingController.php#L39"
            >
                Selectively reload specific props using
                <code class="text-xs">only</code> and
                <code class="text-xs">except</code>.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- Current Props -->
                <FeatureCard
                    title="Current Props"
                    description="Each prop updates independently when reloaded."
                >
                    <div class="space-y-4">
                        <div>
                            <h4 class="mb-1 text-sm font-semibold">Users</h4>
                            <div class="space-y-1">
                                <div
                                    v-for="user in users"
                                    :key="user.id"
                                    class="flex items-center justify-between rounded bg-muted/50 px-2 py-1 text-sm"
                                >
                                    <span>{{ user.name }}</span>
                                    <Badge variant="outline" class="text-xs">{{
                                        user.role
                                    }}</Badge>
                                </div>
                            </div>
                        </div>

                        <div>
                            <h4 class="mb-1 text-sm font-semibold">Stats</h4>
                            <div class="flex gap-3">
                                <Badge variant="secondary"
                                    >Total: {{ stats.total }}</Badge
                                >
                                <Badge variant="secondary"
                                    >Favorites: {{ stats.favorites }}</Badge
                                >
                            </div>
                        </div>

                        <div class="flex items-center gap-4">
                            <div>
                                <span class="text-xs text-muted-foreground"
                                    >Timestamp</span
                                >
                                <p class="font-mono text-sm">{{ timestamp }}</p>
                            </div>
                            <div>
                                <span class="text-xs text-muted-foreground"
                                    >Random</span
                                >
                                <p class="font-mono text-sm">
                                    {{ randomNumber }}
                                </p>
                            </div>
                        </div>
                    </div>
                </FeatureCard>

                <!-- Reload Controls -->
                <FeatureCard title="Reload Controls">
                    <template #description>
                        Use <code class="text-xs">only</code> and
                        <code class="text-xs">except</code> to target specific
                        props.
                    </template>
                    <div class="space-y-4">
                        <div class="space-y-2">
                            <h4 class="text-sm font-semibold">
                                router.reload({ only: [...] })
                            </h4>
                            <div class="flex flex-wrap gap-2">
                                <Button
                                    variant="outline"
                                    size="sm"
                                    @click="
                                        router.reload({
                                            only: ['timestamp', 'randomNumber'],
                                            onSuccess: () =>
                                                log(
                                                    'Reloaded: timestamp + randomNumber',
                                                ),
                                        })
                                    "
                                >
                                    only: timestamp + random
                                </Button>
                                <Button
                                    variant="outline"
                                    size="sm"
                                    @click="
                                        router.reload({
                                            only: ['stats'],
                                            onSuccess: () =>
                                                log('Reloaded: stats'),
                                        })
                                    "
                                >
                                    only: stats
                                </Button>
                                <Button
                                    variant="outline"
                                    size="sm"
                                    @click="
                                        router.reload({
                                            only: ['users'],
                                            onSuccess: () =>
                                                log('Reloaded: users'),
                                        })
                                    "
                                >
                                    only: users
                                </Button>
                            </div>
                        </div>

                        <div class="space-y-2">
                            <h4 class="text-sm font-semibold">
                                router.reload({ except: [...] })
                            </h4>
                            <div class="flex flex-wrap gap-2">
                                <Button
                                    variant="outline"
                                    size="sm"
                                    @click="
                                        router.reload({
                                            except: ['users'],
                                            onSuccess: () =>
                                                log(
                                                    'Reloaded all except: users',
                                                ),
                                        })
                                    "
                                >
                                    except: users
                                </Button>
                                <Button
                                    variant="outline"
                                    size="sm"
                                    @click="
                                        router.reload({
                                            except: ['stats', 'users'],
                                            onSuccess: () =>
                                                log(
                                                    'Reloaded all except: stats + users',
                                                ),
                                        })
                                    "
                                >
                                    except: stats + users
                                </Button>
                            </div>
                        </div>

                        <div class="space-y-2">
                            <h4 class="text-sm font-semibold">Full Reload</h4>
                            <Button
                                @click="
                                    router.reload({
                                        onSuccess: () => log('Full reload'),
                                    })
                                "
                            >
                                Reload All Props
                            </Button>
                        </div>
                    </div>
                </FeatureCard>

                <!-- Event Log -->
                <FeatureCard info-card class="lg:col-span-2" title="Event Log">
                    <template #header-action>
                        <Button variant="ghost" size="sm" @click="eventLog = []"
                            >Clear</Button
                        >
                    </template>
                    <div v-if="eventLog.length" class="space-y-1">
                        <div
                            v-for="(entry, i) in eventLog"
                            :key="i"
                            class="rounded bg-muted px-2 py-1 font-mono text-xs"
                        >
                            {{ entry }}
                        </div>
                    </div>
                    <p v-else class="text-xs text-muted-foreground">
                        Click buttons above to see reload events.
                    </p>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
