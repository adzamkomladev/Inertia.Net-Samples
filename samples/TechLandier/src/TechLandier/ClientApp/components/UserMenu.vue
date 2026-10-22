<script setup lang="ts">
import { computed } from 'vue'
import { Link, router, usePage } from '@inertiajs/vue3'
import { LayoutDashboard, LogOut, ShoppingBag, UserRound } from '@lucide/vue'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'

const page = usePage()
const user = computed(() => page.props.auth.user)
const isAdmin = computed(() => user.value?.role === 'admin')
const initials = computed(() => user.value?.name.split(/\s+/).map((p) => p[0]).slice(0, 2).join('').toUpperCase() ?? '')

const logout = () => {
  router.flushAll()
  router.post('/logout')
}
</script>

<template>
  <DropdownMenu v-if="user">
    <DropdownMenuTrigger as-child>
      <button type="button" class="flex size-11 items-center justify-center rounded-full md:size-10" aria-label="Account menu">
        <Avatar class="size-8">
          <AvatarImage v-if="user.avatarUrl" :src="user.avatarUrl" alt="" />
          <AvatarFallback class="bg-primary text-primary-foreground text-xs font-medium">{{ initials }}</AvatarFallback>
        </Avatar>
      </button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" class="w-60">
      <DropdownMenuLabel class="font-normal">
        <div class="truncate text-sm font-medium">{{ user.name }}</div>
        <div class="text-muted-foreground truncate text-xs">{{ user.email }}</div>
      </DropdownMenuLabel>
      <DropdownMenuSeparator />
      <DropdownMenuItem as-child><Link href="/account" prefetch><UserRound class="size-4" /> My account</Link></DropdownMenuItem>
      <DropdownMenuItem as-child><Link href="/account/orders" prefetch><ShoppingBag class="size-4" /> Orders</Link></DropdownMenuItem>
      <DropdownMenuItem v-if="isAdmin" as-child><Link href="/admin" prefetch><LayoutDashboard class="size-4" /> Admin console</Link></DropdownMenuItem>
      <DropdownMenuSeparator />
      <DropdownMenuItem @select="logout"><LogOut class="size-4" /> Log out</DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
